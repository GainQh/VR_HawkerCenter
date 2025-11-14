//public class BLE_nrf52840: MonoBehaviour
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;

public class BLE_nrf52840 : MonoBehaviour
{
    [Header("Device Filter")]
    public string TargetNameKeyword = "TaoGeschirr";

    [Header("GATT UUIDs (128-bit)")]
    public string ServiceUUID = "A9E90000-194C-4523-A473-5FDF36AA4D20";
    public string RX_CharacteristicUUID = "A9E90001-194C-4523-A473-5FDF36AA4D20"; // Write
    public string TX_CharacteristicUUID = "A9E90002-194C-4523-A473-5FDF36AA4D20"; // Notify -> 0xA5 heartbeat

    [Header("Heartbeat & Retry")]
    public float HeartbeatTimeoutSeconds = 15f;
    public float RescanDelaySeconds = 1.0f;
    [Tooltip("名字连接的超时(秒)，超时后自动改用 address 连接")]
    public float NameConnectFallbackSeconds = 3.0f;

    [Header("UI")]
    public Text StatusText;

    private enum BLEState { Idle, Initializing, Scanning, Connecting, Subscribing, Connected, Deinitializing }
    private BLEState _state = BLEState.Idle;

    // name -> address
    private readonly Dictionary<string, string> _nameToAddr = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private string _currentName = null;
    private string _currentAddr = null;

    private float _lastHeartbeatTime = 0f;
    private readonly Queue<ushort> _pendingBitmaskQueue = new Queue<ushort>();
    private bool _sendingInProgress = false;
    private bool _cleanupInFlight = false;

    private Coroutine _connectFallbackCo = null;
    private bool _connectedCallbackHit = false;

    private void Start()
    {
        BeginInitializeAndScan();
    }

    private void Update()
    {
        if (_state == BLEState.Connected)
        {
            if (Time.time - _lastHeartbeatTime >= HeartbeatTimeoutSeconds)
            {
                StatusTextSet("[TIMEOUT] No heartbeat. Reconnecting...");
                StartFullCleanupAndRescan();
            }
        }
    }

    private void OnDisable() { TryFullDeinit("[OnDisable]"); }
    private void OnApplicationQuit() { TryFullDeinit("[OnQuit]"); }

    // ===== Public send: 2-byte little-endian bitmask =====
    public void SendBitmask(ushort mask)
    {
        if (_state == BLEState.Connected && !string.IsNullOrEmpty(_currentName))
        {
            WriteBitmask(mask);
        }
        else
        {
            _pendingBitmaskQueue.Enqueue(mask);
            if (_state == BLEState.Idle) BeginInitializeAndScan();
        }
    }

    // ===== Init + Scan =====
    private void BeginInitializeAndScan()
    {
        if (_state == BLEState.Initializing || _state == BLEState.Scanning) return;

        _state = BLEState.Initializing;
        _cleanupInFlight = false;
        _connectedCallbackHit = false;
        _currentName = null; _currentAddr = null;
        _nameToAddr.Clear(); _seenNames.Clear();

        StatusTextSet("[INIT] Initializing BLE...");
        BluetoothLEHardwareInterface.Initialize(
            true, false,
            () => { StatusTextSet("[INIT] OK"); StartScan(); },
            (err) => { StatusTextSet("[INIT][ERR] " + err); Invoke(nameof(BeginInitializeAndScan), RescanDelaySeconds); }
        );
    }

    private void StartScan()
    {
        _state = BLEState.Scanning;
        _lastHeartbeatTime = Time.time - HeartbeatTimeoutSeconds;
        StatusTextSet("[SCAN] name contains '" + TargetNameKeyword + "', service " + Short(ServiceUUID));

        string[] services = new string[] { NormalizeUUID(ServiceUUID) };

        BluetoothLEHardwareInterface.ScanForPeripheralsWithServices(
            services,
            (address, name) =>
            {
                if (!IsCandidate(name)) return;
                _nameToAddr[name] = address;

                if (_seenNames.Add(name))
                    StatusTextSet("[FOUND] " + name + " @" + address);

                if (string.IsNullOrEmpty(_currentName))
                {
                    _currentName = name;
                    _currentAddr = address;
                    BluetoothLEHardwareInterface.StopScan();
                    TryConnectSequence(_currentName, _currentAddr);
                }
            },
            (address, name, rssi, adv) =>
            {
                if (!IsCandidate(name)) return;
                _nameToAddr[name] = address;

                if (_seenNames.Add(name))
                    StatusTextSet("[FOUND] " + name + $" (RSSI {rssi})");

                if (string.IsNullOrEmpty(_currentName))
                {
                    _currentName = name;
                    _currentAddr = address;
                    BluetoothLEHardwareInterface.StopScan();
                    TryConnectSequence(_currentName, _currentAddr);
                }
            }
        );
    }

    private bool IsCandidate(string name)
    {
        return !string.IsNullOrEmpty(name) &&
               name.IndexOf(TargetNameKeyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ===== Connect sequence: try NAME first, then fallback to ADDRESS =====
    private void TryConnectSequence(string name, string address)
    {
        _state = BLEState.Connecting;
        _connectedCallbackHit = false;
        StatusTextSet("[CONNECT] " + name + " ...");

        // 1) NAME attempt
        ConnectCore(name);

        // 2) fallback timer
        if (_connectFallbackCo != null) StopCoroutine(_connectFallbackCo);
        _connectFallbackCo = StartCoroutine(FallbackToAddressAfter(NameConnectFallbackSeconds, name, address));
    }

    private IEnumerator FallbackToAddressAfter(float sec, string name, string address)
    {
        yield return new WaitForSeconds(sec);

        if (_state == BLEState.Connecting && !_connectedCallbackHit)
        {
            // 先断掉可能的半连接
            SafeLog("[CONNECT][FALLBACK] Name connect not established. Trying address...");
            try { BluetoothLEHardwareInterface.DisconnectAll(); } catch { }
            yield return new WaitForSeconds(0.2f);

            // 用 address 再连
            StatusTextSet("[CONNECT] " + address + " (by address) ...");
            ConnectCore(address);
        }
    }

    // ConnectToPeripheral(string nameOrAddress, ...)
    private void ConnectCore(string nameOrAddress)
    {
        BluetoothLEHardwareInterface.ConnectToPeripheral(
            nameOrAddress,
            // onConnected
            (who) =>
            {
                _connectedCallbackHit = true;
                StatusTextSet("[CONNECT] Connected: " + who);
            },
            // onServiceDiscovered
            (who, serviceUUID) =>
            {
                if (UuidEqual(serviceUUID, ServiceUUID))
                    SafeLog("[DISCOVER] Service ok: " + serviceUUID);
            },
            // onCharacteristicDiscovered
            (who, serviceUUID, characteristicUUID) =>
            {
                if (!UuidEqual(serviceUUID, ServiceUUID)) return;

                if (UuidEqual(characteristicUUID, TX_CharacteristicUUID))
                {
                    if (_state != BLEState.Subscribing)
                    {
                        _state = BLEState.Subscribing;
                        SubscribeTXByTarget(who);
                    }
                }
                if (UuidEqual(characteristicUUID, RX_CharacteristicUUID))
                {
                    SafeLog("[DISCOVER] RX ok: " + characteristicUUID);
                }
            },
            // onDisconnected
            (who) =>
            {
                StatusTextSet("[DISCONNECT] " + who + " -> rescan");
                StartFullCleanupAndRescan();
            }
        );
    }

    private void SubscribeTXByTarget(string nameOrAddress)
    {
        StatusTextSet("[SUBSCRIBE] TX " + Short(TX_CharacteristicUUID));

        try
        {
            BluetoothLEHardwareInterface.SubscribeCharacteristic(
                nameOrAddress,
                NormalizeUUID(ServiceUUID),
                NormalizeUUID(TX_CharacteristicUUID),
                (characteristicUUID) =>
                {
                    SafeLog("[SUBSCRIBE] State: " + Short(characteristicUUID));
                },
                (characteristicUUID, data) =>
                {
                    if (data != null && data.Length > 0 && data[0] == 0xA5)
                    {
                        _lastHeartbeatTime = Time.time;
                        if (_state != BLEState.Connected)
                        {
                            _state = BLEState.Connected;
                            // 记录当前实际成功连通的名字与地址（若能查到）
                            _currentName = nameOrAddress;
                            if (_nameToAddr.TryGetValue(_currentName, out var addr)) _currentAddr = addr;
                            StatusTextSet("[NOTIFY] A5 -> CONNECTED");
                            TryFlushPendingSends();
                        }
                        else
                        {
                            StatusTextSet("[NOTIFY] A5 @" + _lastHeartbeatTime.ToString("F1"));
                        }
                    }
                }
            );
        }
        catch (Exception e)
        {
            StatusTextSet("[SUBSCRIBE][EXC] " + e.Message);
            StartFullCleanupAndRescan();
        }
    }

    // ===== Write 2-byte bitmask =====
    private void WriteBitmask(ushort mask)
    {
        var target = !string.IsNullOrEmpty(_currentName) ? _currentName : _currentAddr;
        if (string.IsNullOrEmpty(target))
        {
            _pendingBitmaskQueue.Enqueue(mask);
            StartFullCleanupAndRescan();
            return;
        }
        if (_sendingInProgress)
        {
            _pendingBitmaskQueue.Enqueue(mask);
            return;
        }

        _sendingInProgress = true;
        byte[] data = { (byte)(mask & 0xFF), (byte)((mask >> 8) & 0xFF) };
        StatusTextSet("[SEND] bitmask=" + mask + " -> " + Short(RX_CharacteristicUUID));

        try
        {
            BluetoothLEHardwareInterface.WriteCharacteristic(
                target,
                NormalizeUUID(ServiceUUID),
                NormalizeUUID(RX_CharacteristicUUID),
                data,
                data.Length,
                true,
                (characteristicUUID) =>
                {
                    SafeLog("[SEND] OK -> " + Short(characteristicUUID));
                    _sendingInProgress = false;
                    TryFlushPendingSends();
                }
            );
        }
        catch (Exception e)
        {
            SafeLog("[SEND][EXC] " + e.Message + " -> queue & reconnect");
            _sendingInProgress = false;
            _pendingBitmaskQueue.Enqueue(mask);
            StartFullCleanupAndRescan();
        }
    }

    private void TryFlushPendingSends()
    {
        if (_state != BLEState.Connected) return;
        if (_sendingInProgress) return;
        if (_pendingBitmaskQueue.Count > 0)
        {
            var next = _pendingBitmaskQueue.Dequeue();
            WriteBitmask(next);
        }
    }

    // ===== Full cleanup + rescan (clear GATT cache) =====
    private void StartFullCleanupAndRescan()
    {
        if (_cleanupInFlight) return;
        _cleanupInFlight = true;

        _state = BLEState.Deinitializing;
        StatusTextSet("[RESCAN] Full cleanup...");

        try { BluetoothLEHardwareInterface.DisconnectAll(); } catch { }

        TryFullDeinit("[CLEAN]");
        Invoke(nameof(BeginInitializeAndScan), RescanDelaySeconds);
    }

    private void TryFullDeinit(string tag)
    {
        try
        {
            BluetoothLEHardwareInterface.DeInitialize(() =>
            {
                SafeLog($"{tag} DeInitialized");
                StatusTextSet("[CLEAN] DeInitialized");
                _state = BLEState.Idle;
            });
        }
        catch (Exception e)
        {
            SafeLog($"{tag} DeInit EXC: " + e.Message);
            _state = BLEState.Idle;
        }
    }

    // ===== Helpers =====
    private void StatusTextSet(string msg)
    {
        BluetoothLEHardwareInterface.Log(msg);
        Debug.Log(msg);
        if (StatusText != null) StatusText.text = msg;
    }

    private static string NormalizeUUID(string uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return uuid;
        return uuid.Trim().ToUpperInvariant();
    }
    private static bool UuidEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        return NormalizeUUID(a) == NormalizeUUID(b);
    }
    private static string Short(string uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return "";
        string u = NormalizeUUID(uuid);
        if (u.Length <= 8) return u;
        return u.Substring(0, 4) + "..." + u.Substring(u.Length - 4, 4);
    }

    // ===== Helper logging =====
    private void SafeLog(string msg)
    {
        BluetoothLEHardwareInterface.Log(msg);
        Debug.Log(msg);
    }

}
