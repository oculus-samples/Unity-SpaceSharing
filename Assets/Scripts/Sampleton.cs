// Copyright (c) Meta Platforms, Inc. and affiliates.

using JetBrains.Annotations;

using Meta.XR.MRUtilityKit;

using Photon.Pun;

using System.Collections;
using System.Collections.Generic;

using TMPro;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

using Application = UnityEngine.Application;
using StringBuilder = System.Text.StringBuilder;


/// <summary>
/// Central singleton for sample scene setup and shared state.
/// </summary>
public class Sampleton : MonoBehaviour
{
    //
    // Static interface

    public static ConnectMethod ConnectMethod
        => s_Instance ? s_Instance.m_Connexion : ConnectMethod.None;

    public static Scene CurrentScene
        => s_Instance ? s_Instance.gameObject.scene : SceneManager.GetActiveScene();

    [NotNull]
    public static Transform PlayerFace
    {
        get
        {
            Assert.IsNotNull(s_Instance, "No Sampleton in the scene!");
            Assert.IsNotNull(Camera.main, "No Camera in the scene!");
            if (!s_Instance.m_PlayerFace)
            {
                // this is a runtime fallback; m_PlayerFace should keep a serialized ref
                s_Instance.m_PlayerFace = Camera.main.transform;
            }
            return s_Instance.m_PlayerFace;
        }
    }

    [CanBeNull]
    public static BaseUI BaseUI
        => s_Instance ? s_Instance.m_MenuUI : null;

    [CanBeNull]
    public static PhotonRoomManager PhotonRoomManager
        => s_Instance ? s_Instance.m_PhotonMan : null;

    /// <summary>
    ///     This is used by a runtime UI Toggle's UnityEvent to allow toggling
    ///     MRUK's world locking implementation on or off.
    /// </summary>
    /// <remarks>
    ///     Scene mesh renderers on = recommended.
    ///     Otherwise, you can use the scene's origin indicator as a reference.
    /// </remarks>
    public static bool DoWorldLocking
    {
        get => MRUK.Instance && MRUK.Instance.EnableWorldLock;
        set
        {
            if (MRUK.Instance)
                MRUK.Instance.EnableWorldLock = value;
        }
    }


    [ContractAnnotation("=> baseUI:null, false ; => baseUI:notnull, true")]
    public static bool GetActiveUI(out BaseUI baseUI)
    {
        if (s_Instance)
        {
            baseUI = s_Instance.m_MenuUI;
            return baseUI;
        }

        baseUI = null;
        return false;
    }

    public static string GetNickname()
    {
        string nickname = PhotonNetwork.NickName;
        if (string.IsNullOrEmpty(nickname))
        {
            PhotonNetwork.NickName = nickname = $"TestUser{Random.Range(0, 10000):0000}";
        }
        return nickname;
    }

    public static void SetNickname(string nickname)
    {
        var prev = PhotonNetwork.NickName;
        PhotonNetwork.NickName = nickname;
        if (string.IsNullOrEmpty(prev))
            Log($"Set NickName: \"{nickname}\"");
        else if (prev != nickname)
            Log($"Overriding Nickname: \"{prev}\" -> \"{nickname}\"");
    }

    public static Coroutine DelayCall(float seconds, System.Action action)
    {
        return DelayCall(new WaitForSeconds(seconds), action);
    }

    public static Coroutine DelayCall(object delay, System.Action action)
    {
        if (!s_Instance || !s_Instance.isActiveAndEnabled)
        {
            Warn($"{nameof(DelayCall)} called with invalid Sampleton state. Invocation will be immediate.");
            action?.Invoke();
            return null;
        }

        return s_Instance.StartCoroutine(coroutine(action, delay));

        static IEnumerator coroutine(System.Action payload, object delay)
        {
            yield return delay;
            payload?.Invoke();
        }
    }


    //
    // Static logging interface

    public static void Log(object message, LogType type = LogType.Log, bool trace = false)
    {
        // Console logging (goes to logcat on device)

        var msgStr = message?.ToString() ?? string.Empty;

        Debug.LogFormat(
            logType: type,
            logOptions: trace ? LogOption.None : LogOption.NoStacktrace,
            context: null,
            format: $"{Application.productName}: {msgStr}"
        );

        if (s_Instance)
            s_Instance.LogInScene(msgStr, type);
    }

    public static void Log(object message, bool error)
    {
        if (error)
            Error(message);
        else
            Log(message);
    }

    public static void Error(object message)
        => Log(message, type: LogType.Error, trace: true);

    public static void Warn(object message)
        => Log(message, type: LogType.Warning, trace: true);


    // private

    static Sampleton s_Instance;
    static Oculus.Platform.Models.User s_OculusUser;
    static float s_UnfocusedAt = float.MaxValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StaticAwake()
    {
        // Setup static event listeners
        Application.focusChanged += appFocusChanged;

        // init log with meta info
        LogBasicInfo();

        // init Oculus.Platform.Core + GetLoggedInUser (for username, scoped OC_ID)
        Log($"Oculus.Platform.Core.Initialize()...");
        try
        {
            // API call: static Oculus.Platform.Core.Initialize()
            Oculus.Platform.Core.Initialize();
        }
        catch (UnityException e)
        {
            Warn($"Oculus.Platform.Core.Initialize FAILED: {e.Message}");
            // "UnityException: Update your app id by selecting 'Meta' > 'Platform > 'Edit Settings'"
        }

        if (Oculus.Platform.Core.IsInitialized())
        {
            // API call: static Oculus.Platform.Users.GetLoggedInUser()
            Oculus.Platform.Users.GetLoggedInUser().OnComplete(receiveLoggedInUser);
            return;
        }

        Warn(
            $"NOTICE: Platform SDK is not initialized.\n" +
            $" - A fake username has been generated for you: {GetNickname()}"
        );
        Log(
            " ++ Unity-SpaceSharing only uses Platform usernames for informational purposes. " +
            "Everything in this sample should work without valid entitlements!"
        );

        return;

        // local methods:

        static void receiveLoggedInUser(Oculus.Platform.Message msg)
        {
            const Oculus.Platform.Message.MessageType kExpectType
                = Oculus.Platform.Message.MessageType.User_GetLoggedInUser;

            Assert.IsNotNull(msg, "msg != null");
            Assert.AreEqual(kExpectType, msg.Type, $"{nameof(receiveLoggedInUser)}: Unexpected message type {msg.Type}");

            if (msg.IsError)
            {
                var err = msg.GetError();
                var codeStr = err.Code switch
                {
                    2 => $"AUTHENTICATION_ERROR({err.Code})",
                    3 => $"NETWORK_ERROR({err.Code})",
                    4 => $"STORE_INSTALLATION_ERROR({err.Code})",
                    5 => $"CALLER_NOT_SIGNED({err.Code})",
                    6 => $"UNKNOWN_SERVER_ERROR({err.Code})",
                    7 => $"PERMISSIONS_FAILURE({err.Code})",
                    _ => $"UNKNOWN_ERROR({err.Code})"
                };
                Error($"{nameof(receiveLoggedInUser)} FAILED: code={codeStr} message=\"{err.Message}\"");
#if UNITY_EDITOR // PC Link
                if (err.Code == 1)
                    Log("  -> Did you remember to enter your test user credentials in the OculusPlatformSettings asset?");
#endif
                return;
            }

            var flume = new StringBuilder($"{nameof(receiveLoggedInUser)}: {msg.Type}");

            s_OculusUser = msg.GetUser();

            Assert.IsNotNull(s_OculusUser, "user != null");

            var username = s_OculusUser.OculusID;
            var ocUserId = s_OculusUser.ID;

            flume.Append($"\n+ Platform Username: {username}");
            flume.Append($"\n+ Platform User ID: {ocUserId}");

            Log(flume);

            SetNickname(username);

            if (ocUserId > 0)
                return;

            // ocUserId isn't used for much else in this sample.

            Warn(
                $"NOTICE: Your user/device is not authenticated to use this app.\n" +
                $" - A fake username has been generated for you: {GetNickname()}"
            );
            Log(
                " ++ Unity-SpaceSharing only uses Platform usernames for informational purposes. " +
                "Everything in this sample should work without valid entitlements!"
            );
        }

        static void appFocusChanged(bool isFocused)
        {
            float now = Time.realtimeSinceStartup;
            if (isFocused)
            {
                if (s_UnfocusedAt < now)
                    Log($"<< App regained focus. (t+{now - s_UnfocusedAt:F1}s delta)");
                return;
            }

            Log($">> App lost focus / HMD has gone idle. t={now:F1}s");
            s_UnfocusedAt = now;
        }
    }

    static void LogBasicInfo()
    {
        Log($"oculus-samples/{Application.productName}   v{Application.version}");
        Log($"{System.DateTime.Now:f}");
        Log($"Core Major Version: \t{OVRPlugin.version.Minor - 32}");
        Log($"OVRPlugin.version:  \t{OVRPlugin.version}");
        Log($"Photon PUN Version: \t{PhotonNetwork.PunVersion}");
        s_LogBuilder.Append("\n");
    }


    //
    // Serialized fields

    [SerializeField]
    ConnectMethod m_Connexion;

    [Header("In-Scene Logging")]
    [SerializeField]
    TextMeshProUGUI m_LogText;
    [SerializeField]
    TextMeshProUGUI m_PageLabel;

    [Header("Auto Refs")]
    [SerializeField]
    BaseUI m_MenuUI;
    [SerializeField]
    PhotonRoomManager m_PhotonMan;
    [SerializeField]
    Transform m_PlayerFace;


    //
    // MonoBehaviour messages

    void OnValidate()
    {
        if (!m_MenuUI)
        {
            m_MenuUI = GetComponentInChildren<BaseUI>();
        }

        if (!gameObject.scene.isLoaded) // anti-prefab guard
            return;

        foreach (var rootObj in gameObject.scene.GetRootGameObjects())
        {
            if (m_PhotonMan && m_PlayerFace)
                break;

            if (!m_PhotonMan && rootObj.TryGetComponent(out PhotonRoomManager photonMan))
                m_PhotonMan = photonMan;

            if (!m_PlayerFace && rootObj.TryGetComponent(out OVRCameraRig rig))
                m_PlayerFace = rig.centerEyeAnchor;
        }
    }

    void OnEnable()
    {
        Assert.IsNull(s_Instance, $"{nameof(Sampleton)}.s_Instance");
        s_Instance = this;

        if (!m_MenuUI)
            return;

        m_MenuUI.OnDisplayLobby += LogEnd;
        m_MenuUI.OnDisplayRoom += LogEnd;
    }

    void OnDisable()
    {
        if (s_Instance == this)
            s_Instance = null;

        s_LogBuilder.Append("\n");

        if (!m_MenuUI)
            return;

        m_MenuUI.OnDisplayLobby -= LogEnd;
        m_MenuUI.OnDisplayRoom -= LogEnd;
    }

    void Awake()
    {
        // setup connexion & UI objects
        switch (m_Connexion)
        {
            case ConnectMethod.None:
                if (!m_MenuUI)
                {
                    m_MenuUI = gameObject.AddComponent<BaseUI>();
                }
                break;
            case ConnectMethod.Photon:
                if (!m_MenuUI)
                {
                    m_MenuUI = gameObject.AddComponent<LocalSpaceSharingUI>();
                }
                if (!m_PhotonMan)
                {
                    m_PhotonMan = new GameObject(nameof(PhotonRoomManager)).AddComponent<PhotonRoomManager>();
                }
                break;

            case ConnectMethod.ColocationSession:
                if (!m_MenuUI)
                {
                    throw new System.NotImplementedException();
                }
                break;
        }

        // ensure logs start at the last page
        LogEnd();
    }

    void LateUpdate()
    {
        UpdateLogText();
    }


    //
    // UnityEvent-compatible interface

    public static void LoadScene(int buildIdx)
    {
        if (buildIdx >= 0 && buildIdx < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(buildIdx);
            Log($"{nameof(LoadScene)}({buildIdx}): {SceneManager.GetSceneByBuildIndex(buildIdx).name}");
        }
        else if (SceneManager.GetActiveScene().buildIndex == 0)
        {
            Error($"{nameof(LoadScene)}({buildIdx}): Scene build index out of range.");
        }
        else
        {
            Error($"{nameof(LoadScene)}({buildIdx}): Scene build index out of range.\nReturning to Scene 0 in 3 seconds.");
            DelayCall(3f, () => SceneManager.LoadScene(0));
        }
    }

    public static void ExitAppOrPlaymode()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();
#else
        Application.Quit();
#endif
    }

    public static void GoBack()
    {
        switch (ConnectMethod)
        {
            case ConnectMethod.None:
                ExitAppOrPlaymode();
                break;

            case ConnectMethod.Photon:
                if (PhotonNetwork.InRoom)
                {
                    PhotonNetwork.LeaveRoom(becomeInactive: false);
                }
                else
                {
                    PhotonNetwork.Disconnect();
                    SceneManager.LoadScene(0);
                }
                break;

            default:
            case ConnectMethod.ColocationSession:
                SceneManager.LoadScene(0);
                break;
        }
    }


    public static void Trace(string message)
    {
        Log(message, trace: true);
    }

    public static void LogClear(bool relogBasicInfo = true)
    {
        s_LogBuilder.Clear();

        if (relogBasicInfo)
            LogBasicInfo();
        else if (s_Instance)
            s_Instance.UpdateLogText();
    }


    public void LogNext()
    {
        if (!m_LogText)
            return;

        int z = m_LogText.textInfo.pageCount;
        int p = m_LogText.pageToDisplay + 1;
        if (p > z)
            p = z;

        m_LogText.pageToDisplay = p;
    }

    public void LogPrev()
    {
        if (!m_LogText)
            return;

        int p = m_LogText.pageToDisplay - 1;
        if (p < 1)
            p = 1;

        m_LogText.pageToDisplay = p;
    }

    public void LogEnd()
    {
        // need to yield an extra frame to properly account for layout recalcs~
        _ = StartCoroutine(coroutine());

        return;

        IEnumerator coroutine()
        {
            if (!m_LogText)
                yield break;
            yield return null;
            m_LogPage = -1;
            m_LogText.pageToDisplay = 0;
        }
    }


    //
    // LogInScene impl.

    static readonly StringBuilder s_LogBuilder = new();
    static readonly Dictionary<LogType, string> s_LogColors = new()
    {
        [LogType.Warning] = "<color=#FEFF00>",
        [LogType.Error] = "<color=#CA2622>",
        [LogType.Exception] = "<color=#CA2622>",
        [LogType.Assert] = "<color=#CA2622>",
    };

    int m_LogSize = -1;
    int m_LogPage = -1;

    void LogInScene(string message, LogType type)
    {
        // In VR Logging
        if (string.IsNullOrEmpty(message))
            return;

        if (s_LogBuilder.Length > 0)
            s_LogBuilder.Append('\n');

        bool doColor = s_LogColors.TryGetValue(type, out string colorTag);
        if (doColor)
            s_LogBuilder.Append(colorTag);

        s_LogBuilder.Append(message);

        if (doColor)
            s_LogBuilder.Append("</color>");
    }

    void UpdateLogText()
    {
        if (!m_LogText)
            return;

        int p = m_LogText.pageToDisplay;
        if (p == m_LogPage && m_LogSize == s_LogBuilder.Length && Time.frameCount % 101 != 0)
            return;

        int prevZ = m_LogText.textInfo?.pageCount ?? p;
        if (prevZ < 1)
            prevZ = p;

        bool trackLastPage = p < 1 || p == prevZ;

        var textInfo = m_LogText.GetTextInfo(s_LogBuilder.ToString());
        // oddly enough, GetTextInfo calls SetText internally~

        // m_LogText.SetText(s_LogBuilder);
        // m_LogText.ForceMeshUpdate(); // so that pageCount is correctly updated
        // Assert.IsNotNull(m_LogText.textInfo, "m_LogText.textInfo != null");

        m_LogSize = s_LogBuilder.Length;

        int z = textInfo.pageCount;
        if (trackLastPage || p > z)
            p = z;

        m_LogText.pageToDisplay = p;
        m_LogPage = p;

        if (m_PageLabel)
            m_PageLabel.SetText($"{p}/{z}");
    }
}
