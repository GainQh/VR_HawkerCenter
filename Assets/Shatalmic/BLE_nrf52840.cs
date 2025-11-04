using UnityEngine;
using UnityEngine.UI;
using System;

public class BLE_nrf52840 : MonoBehaviour
{
    public string DeviceName = "ledbtn";
    public string ServiceUUID = "A9E90000-194C-4523-A473-5FDF36AA4D20";
    public string CharactristicUUID = "A9E90001-194C-4523-A473-5FDF36AA4D20";

    // ????
    public float HeartbeatInterval = 1.0f;   // ???????ping????
    public float HeartbeatTimeout = 3.0f;   // ?????????????

    enum States
    {
        None,
        Scan,
        ScanRSSI,
        ReadRSSI,      // ???????
        Connect,
        KeepAlive,
        Disconnect
    }

    private bool _connected = false;
    private float _timeout = 0f;
    private States _state = States.None;
    private string _deviceAddress;
    private bool _rssiOnly = false;
    private int _rssi = 0;

    // ????
    private float _lastHeartbeatTime = 0f;   // ????????????
    private float _lastSeenTime = 0f;        // ?????????????

    public Text StatusText;
    public Text ButtonPositionText;

    private string StatusMessage
    {
        set
        {
            BluetoothLEHardwareInterface.Log(value);
            if (StatusText != null)
                StatusText.text = value;
        }
    }

    void Reset()
    {
        _connected = false;
        _timeout = 0f;
        _state = States.None;
        _deviceAddress = null;
        _rssi = 0;

        _lastHeartbeatTime = 0f;
        _lastSeenTime = 0f;
    }

    void SetState(States newState, float timeout)
    {
        _state = newState;
        _timeout = timeout;
    }

    void StartProcess()
    {
        Reset();
        BluetoothLEHardwareInterface.Initialize(true, false,
        () =>
        {
            StatusMessage = "[INIT] BLE initialized";
            SetState(States.Scan, 0.1f);
        },
        (error) =>
        {
            StatusMessage = "[INIT][ERR] " + error;
        });
    }

    void Start()
    {
        StartProcess();
    }

    void Update()
    {
        // ???????
        if (_timeout > 0f)
        {
            _timeout -= Time.deltaTime;
            if (_timeout <= 0f)
            {
                _timeout = 0f;

                switch (_state)
                {
                    case States.None:
                        break;

                    case States.Scan:
                        StatusMessage = "[SCAN] Scanning for " + DeviceName;

                        BluetoothLEHardwareInterface.ScanForPeripheralsWithServices(
                            null,
                            (address, name) =>
                            {
                                if (!string.IsNullOrEmpty(name) && name.Contains(DeviceName))
                                {
                                    StatusMessage = "[SCAN] Found " + name + " at " + address;
                                    _deviceAddress = address;

                                    BluetoothLEHardwareInterface.StopScan();
                                    SetState(States.Connect, 0.2f);
                                }
                            },
                            (address, name, rssi, bytes) =>
                            {
                                if (!string.IsNullOrEmpty(name) && name.Contains(DeviceName))
                                {
                                    StatusMessage = "[SCAN] Found " + name + " (RSSI mode) at " + address;
                                    _deviceAddress = address;

                                    BluetoothLEHardwareInterface.StopScan();
                                    SetState(States.Connect, 0.2f);
                                }
                            },
                            _rssiOnly
                        );

                        if (_rssiOnly)
                            SetState(States.ScanRSSI, 0.5f);

                        break;

                    case States.Connect:
                        if (string.IsNullOrEmpty(_deviceAddress))
                        {
                            StatusMessage = "[CONNECT][ERR] no device address, back to scan";
                            SetState(States.Scan, 0.5f);
                            break;
                        }

                        StatusMessage = "[CONNECT] Connecting to " + _deviceAddress;

                        // ????????? (addr, onConnected, onServiceDiscovered, onCharacteristicDiscovered)
                        BluetoothLEHardwareInterface.ConnectToPeripheral(
                            _deviceAddress,
                            // onConnected
                            (address) =>
                            {
                                StatusMessage = "[CONNECT] Connected to " + address;
                                _connected = true;

                                // ????????????
                                _lastSeenTime = Time.time;
                            },
                            // onServiceDiscovered
                            (address, serviceUUID) =>
                            {
                                if (IsEqual(serviceUUID, ServiceUUID))
                                {
                                    StatusMessage = "[CONNECT] Service OK: " + serviceUUID;
                                    _connected = true;
                                    _lastSeenTime = Time.time;

                                    // ?? KeepAlive
                                    SetState(States.KeepAlive, 0.0f);
                                }
                            },
                            // onCharacteristicDiscovered
                            (address, serviceUUID, characteristicUUID) =>
                            {
                                if (IsEqual(serviceUUID, ServiceUUID) &&
                                    IsEqual(characteristicUUID, CharactristicUUID))
                                {
                                    StatusMessage = "[CONNECT] Char OK: " + characteristicUUID;
                                    _connected = true;
                                    _lastSeenTime = Time.time;

                                    SetState(States.KeepAlive, 0.0f);
                                }
                            }
                        );

                        break;

                    case States.KeepAlive:
                        // ??? KeepAlive ?????????
                        HandleKeepAlive();
                        break;

                    case States.ReadRSSI:
                        // ?????RSSI
                        DoHeartbeatReadRSSI();
                        break;

                    case States.ScanRSSI:
                        // ???????ScanRSSI????????????
                        break;

                    case States.Disconnect:
                        StatusMessage = "[DISCONNECT] Forcing disconnect";

                        if (_connected && !string.IsNullOrEmpty(_deviceAddress))
                        {
                            string addrCopy = _deviceAddress;
                            BluetoothLEHardwareInterface.DisconnectPeripheral(addrCopy, (address) =>
                            {
                                StatusMessage = "[DISCONNECT] Disconnected " + address;
                                BluetoothLEHardwareInterface.DeInitialize(() =>
                                {
                                    _connected = false;
                                    _deviceAddress = null;
                                    _state = States.None;
                                });
                            });
                        }
                        else
                        {
                            BluetoothLEHardwareInterface.DeInitialize(() =>
                            {
                                _state = States.None;
                            });
                        }
                        break;
                }
            }
        }

        // ? Update ?????????????????????
        if (_state == States.KeepAlive && _connected)
        {
            float now = Time.time;
            if (now - _lastSeenTime > HeartbeatTimeout)
            {
                // ????????????
                StatusMessage = "[KEEPALIVE][TIMEOUT] Lost device. Rescanning...";
                ForceDisconnectAndRescan();
            }
        }
    }

    // ----------------------
    // ???????
    // ----------------------

    private void HandleKeepAlive()
    {
        // ??????????????RSSI??
        if (_connected && !string.IsNullOrEmpty(_deviceAddress))
        {
            float now = Time.time;
            if (now - _lastHeartbeatTime >= HeartbeatInterval)
            {
                _lastHeartbeatTime = now;
                StatusMessage = "[KEEPALIVE] Heartbeat -> ReadRSSI";
                SetState(States.ReadRSSI, 0.0f);
            }
            else
            {
                // ??????????????KeepAlive
                SetState(States.KeepAlive, 0.1f);
            }
        }
        else
        {
            // ??????? _connected = false
            StatusMessage = "[KEEPALIVE] Not connected. Back to scan.";
            ForceDisconnectAndRescan();
        }
    }

    private void DoHeartbeatReadRSSI()
    {
        if (!_connected || string.IsNullOrEmpty(_deviceAddress))
        {
            StatusMessage = "[HEARTBEAT][ERR] no addr, rescan";
            ForceDisconnectAndRescan();
            return;
        }

        // ????? ReadRSSI
        // ??????????????????????
        // ??? try/catch ??? Unity ?????
        bool callbackCalled = false;
        try
        {
            BluetoothLEHardwareInterface.ReadRSSI(_deviceAddress, (address, rssi) =>
            {
                callbackCalled = true;
                _rssi = rssi;
                _connected = true;
                _lastSeenTime = Time.time; // ? ???????

                StatusMessage = "[HEARTBEAT] RSSI " + rssi + " OK";

                // ??KeepAlive???????
                SetState(States.KeepAlive, HeartbeatInterval * 0.5f);
            });
        }
        catch (Exception e)
        {
            StatusMessage = "[HEARTBEAT][EXC] " + e.Message + " -> assume disconnected.";
            ForceDisconnectAndRescan();
            return;
        }

        // ??????????????catch????
        // ????????????????????????????????????? HeartbeatTimeout ????
        if (!callbackCalled)
        {
            // ???????????? HeartbeatTimeout ??
            SetState(States.KeepAlive, HeartbeatInterval * 0.5f);
        }
    }

    private void ForceDisconnectAndRescan()
    {
        _connected = false;

        // ??????????????????“???????”?
        if (!string.IsNullOrEmpty(_deviceAddress))
        {
            string addrCopy = _deviceAddress;
            BluetoothLEHardwareInterface.DisconnectPeripheral(addrCopy, (address) =>
            {
                StatusMessage = "[FORCE] DisconnectPeripheral called for " + address;
            });
        }

        _deviceAddress = null;

        // ????
        SetState(States.Scan, 0.5f);
    }

    // ----------------------
    // UI / send data
    // ----------------------

    private bool ledON = false;

    public void OnLED()
    {
        ledON = !ledON;
        SendByte(ledON ? (byte)0x01 : (byte)0x00);
    }

    void SendByte(byte value)
    {
        if (!_connected || string.IsNullOrEmpty(_deviceAddress))
        {
            StatusMessage = "[SEND][ERR] Not connected, can't send byte.";
            return;
        }

        byte[] data = { value };

        BluetoothLEHardwareInterface.WriteCharacteristic(
            _deviceAddress,
            ServiceUUID,
            CharactristicUUID,
            data,
            data.Length,
            true,
            (characteristicUUID) =>
            {
                BluetoothLEHardwareInterface.Log("[SEND] Write Succeeded");
            });
    }

    public void SendBitmask(ushort mask)
    {
        if (!_connected || string.IsNullOrEmpty(_deviceAddress))
        {
            StatusMessage = "[SEND][ERR] Not connected, can't send bitmask.";
            return;
        }

        byte[] data = new byte[]
        {
            (byte)(mask & 0xFF),
            (byte)((mask >> 8) & 0xFF)
        };

        BluetoothLEHardwareInterface.WriteCharacteristic(
            _deviceAddress,
            ServiceUUID,
            CharactristicUUID,
            data,
            data.Length,
            true,
            (characteristicUUID) =>
            {
                BluetoothLEHardwareInterface.Log("[SEND] Write Succeeded");
            });
    }

    // ----------------------
    // Helpers
    // ----------------------
    bool IsEqual(string uuid1, string uuid2)
    {
        return uuid1 != null && uuid2 != null && uuid1.ToUpper().Equals(uuid2.ToUpper());
    }
}
