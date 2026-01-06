// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;

using Photon.Pun;
using Photon.Realtime;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Assertions;

using PeerStateValue = ExitGames.Client.Photon.PeerStateValue;
using Hashtable = ExitGames.Client.Photon.Hashtable;


/// <summary>
/// Manages Photon Room creation and maintenance, including custom data synchronized (shared) to the Room.
/// </summary>
[RequireComponent(typeof(PhotonView))]
[MetaCodeSample("SpaceSharing")]
public class PhotonRoomManager : MonoBehaviourPunCallbacks
{
    //
    // Public interface

    public delegate void RoomDataUpdated(Guid groupId, Guid[] roomIds, Pose? floorPose, bool isLocal);
    public delegate void CustomDataUpdated(object boxedData, bool isLocal);


    public static event RoomDataUpdated OnRoomDataUpdated
    {
        add
        {
            if (value is null)
                return;

            if (TryGetRoomData(out var group, out var rooms, out var floorPose, out bool isLocal))
                value(group, rooms, floorPose, isLocal);

            s_OnRoomDataUpdated += value;
        }
        remove => s_OnRoomDataUpdated -= value;
    }

    public static event CustomDataUpdated OnCustomDataUpdated
    {
        add
        {
            if (value is null)
                return;

            if (TryGetCustomData(out var boxedData, out bool isLocal))
                value(boxedData, isLocal);

            s_OnCustomDataUpdated += value;
        }
        remove => s_OnCustomDataUpdated -= value;
    }

    public static string CurrentRoomName
        => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name
                                : PhotonNetwork.InLobby ? $"{PhotonNetwork.CurrentLobby.Name} (lobby)"
                                                        : "(none)";

    public static bool CheckConnection(bool tryReconnect, bool logOnFail)
    {
        if (PhotonNetwork.IsConnected)
            return true;

        var peerState = PhotonNetwork.NetworkingClient.LoadBalancingPeer.PeerState;
        bool reconnecting = peerState == PeerStateValue.Connecting ||
                            (tryReconnect && (PhotonNetwork.Reconnect() ||
                                              PhotonNetwork.ConnectUsingSettings()));
        if (!logOnFail)
            return false;

        const string kPhotonNotConnected = "PhotonNetwork was disconnected";
        const string kNextStepsMsg = "Check your wifi, restart the scene/app, or check logcat for causes";

        if (reconnecting)
            Sampleton.Warn($"{kPhotonNotConnected}. Reconnecting now...\n(Status update should log here in a few moments.)");
        else if (tryReconnect)
            Sampleton.Error($"{kPhotonNotConnected}. The attempt to reconnect FAILED.\n{kNextStepsMsg}.");
        else
            Sampleton.Error($"{kPhotonNotConnected}. {kNextStepsMsg}.");

        return false;
    }

    public static bool TryJoinLobby()
    {
        if (!CheckConnection(tryReconnect: false, logOnFail: false))
            return false;

        s_LastLobby = new TypedLobby(Sampleton.CurrentScene.name, LobbyType.Default);

        return PhotonNetwork.JoinLobby(s_LastLobby);
    }

    public static bool TryJoinRoom(string roomName)
    {
        var kRoomOptions = new RoomOptions()
        {
            IsVisible = true,
            IsOpen = true,
            BroadcastPropsChangeToAll = true,
            MaxPlayers = 0,       // no defined limit
            EmptyRoomTtl = 60000, // 1 minute
            PlayerTtl = 600000,   // 10 minutes
        };
        return PhotonNetwork.JoinOrCreateRoom(roomName, kRoomOptions, s_LastLobby);
    }

    /// <param name="roomUuids">
    ///     Note: The first element is expected to be the Anchor.Uuid of the host's "main" (or current) MRUKRoom.
    /// </param>
    /// <param name="floorPose">
    ///     If provided and not null, must be the world pose of the floor anchor corresponding to the first MRUKRoom
    ///     UUID in <paramref name="roomUuids"/> (from the publisher's perspective). This will allow MRUK to
    ///     automatically perform colocated world alignment, enabling even non-anchor GameObjects to appear in the
    ///     "correct" poses in the shared mixed reality space for all clients.
    /// </param>
    public static void PublishRoomData(Guid groupUuid, ICollection<Guid> roomUuids, Pose? floorPose = null)
    {
        if (floorPose is null)
            Sampleton.Log($"{nameof(PublishRoomData)}: 1 group, {roomUuids.Count} room(s)");
        else
            Sampleton.Log($"{nameof(PublishRoomData)}: 1 group, {roomUuids.Count} room(s), 1 pose");

        if (roomUuids.Count == 0)
        {
            Sampleton.Warn($"- (skipped ~ 0 rooms)");
            return;
        }

        PrepareRoomPropertyUpdate(out var room, out var newProps, out var expected, out int version);

        var roomArr = new Guid[roomUuids.Count];
        roomUuids.CopyTo(roomArr, 0);

        newProps[k_PropKeyRoomIds] = roomArr;
        newProps[k_PropKeyGroupId] = groupUuid;

        if (floorPose.HasValue)
            newProps[k_PropKeyHostPose] = floorPose.Value;
        else if (version > 0)
            newProps[k_PropKeyHostPose] = null;

        if (room.SetCustomProperties(newProps, expected))
            s_LastVersion = version;
        else
            Sampleton.Error("- ERR: Photon room.SetCustomProperties failed! (possible concurrency failure)");
    }

    /// <param name="boxedData">
    ///     MUST be serializable by Photon!
    /// </param>
    public static void PublishCustomData(object boxedData)
    {
        Sampleton.Log($"{nameof(PublishCustomData)}: {nameof(boxedData)}<{boxedData.SafeTypeName()}>");

        PrepareRoomPropertyUpdate(out var room, out var newProps, out var expected, out int version);

        newProps[k_PropKeyCustomData] = boxedData;

        if (room.SetCustomProperties(newProps, expected))
            s_LastVersion = version;
        else
            Sampleton.Error("- ERR: Photon room.SetCustomProperties failed! (possible concurrency failure)");
    }


    //
    // Constants

    const string k_PropKeyVersion = "ver";      // int
    const string k_PropKeySender = "src";       // string
    const string k_PropKeyGroupId = "group";    // System.Guid (works thanks to our PhotonExtensions.cs)
    const string k_PropKeyRoomIds = "rooms";    // System.Guid[] (^) (first Guid = host's main room)
    const string k_PropKeyHostPose = "pose";    // UnityEngine.Pose (^^) (can be omitted)
    const string k_PropKeyCustomData = "data";  // any (listeners to OnCustomDataUpdated decide how to handle)


    //
    // Fields

    static TypedLobby s_LastLobby;

    static string s_LastRoomName;

    static int s_LastVersion = -1;

    static RoomDataUpdated s_OnRoomDataUpdated = LogOnRoomDataUpdated;

    static CustomDataUpdated s_OnCustomDataUpdated = LogOnCustomDataUpdated;


    //
    // impl.

    static void PrepareRoomPropertyUpdate(out Room room, out Hashtable newProps, out Hashtable expectedProps, out int currentVersion)
    {
        room = PhotonNetwork.CurrentRoom;
        Assert.IsNotNull(room, "PhotonNetwork.CurrentRoom");

        currentVersion = 0;
        expectedProps = null;

        if (room.CustomProperties.TryGetValue(k_PropKeyVersion, out var box) && box is int v)
        {
            expectedProps = new Hashtable
            {
                [k_PropKeyVersion] = currentVersion = v,
            };
        }

        newProps = new Hashtable
        {
            [k_PropKeyVersion] = currentVersion + 1,
            [k_PropKeySender] = $"{PhotonNetwork.LocalPlayer}",
        };
    }

    static void LogOnRoomDataUpdated(Guid groupId, Guid[] roomIds, Pose? floorPose, bool isLocal)
    {
        Assert.IsNotNull(Sampleton.PhotonRoomManager, "Sampleton.PhotonRoomManager");

        if (!PhotonNetwork.InRoom ||
            !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(k_PropKeySender, out var box) ||
            box is not string updatedBy)
        {
            updatedBy = "(unknown)";
        }

        Sampleton.Log(
            $"{nameof(OnRoomDataUpdated)}: {nameof(updatedBy)}: {updatedBy}\n" +
            $"    {nameof(isLocal)}: {isLocal}\n" +
            $"    {nameof(groupId)}: {groupId.Brief()}\n" +
            $"    {nameof(roomIds)}.Length: {roomIds.Length}, {nameof(roomIds)}[0]: {roomIds[0].Brief()}\n" +
            $"    {nameof(floorPose)}: {(floorPose is null ? "null" : floorPose.Value.position.ToString("F2"))}"
        // (logging just the floorPose's position is enough to get an idea of it)
        );
    }

    static void LogOnCustomDataUpdated(object boxedData, bool isLocal)
    {
        Assert.IsNotNull(Sampleton.PhotonRoomManager, "Sampleton.PhotonRoomManager");

        if (!PhotonNetwork.InRoom ||
            !PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(k_PropKeySender, out var box) ||
            box is not string updatedBy)
        {
            updatedBy = "(unknown)";
        }

        Sampleton.Log(
            $"{nameof(OnCustomDataUpdated)}: {nameof(updatedBy)}: {updatedBy}\n" +
            $"    {nameof(isLocal)}: {isLocal}\n" +
            $"    {nameof(boxedData)}: <{boxedData.SafeTypeName()}>"
        );
    }


    static bool TryGetRoomData(out Guid groupId, out Guid[] roomIds, out Pose? floorPose, out bool isLocal, Hashtable props = null)
    {
        groupId = Guid.Empty;
        roomIds = Array.Empty<Guid>();
        floorPose = null;
        isLocal = false;

        if (props is null)
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room is null)
                return false;
            props = room.CustomProperties;
        }

        if (!props.TryGetValue(k_PropKeyGroupId, out var box) || box is not Guid groupUuid || groupUuid == Guid.Empty)
            return false;
        groupId = groupUuid;

        if (!props.TryGetValue(k_PropKeyRoomIds, out box) || box is not Guid[] roomArray || roomArray.Length == 0)
            return false;
        roomIds = roomArray;

        if (props.TryGetValue(k_PropKeyHostPose, out box) && box is Pose pose)
            floorPose = pose;

        if (props.TryGetValue(k_PropKeyVersion, out box) && box is int version)
            isLocal = version == s_LastVersion;

        return true;
    }

    static bool TryGetCustomData(out object boxedData, out bool isLocal, Hashtable props = null)
    {
        isLocal = false;

        if (props is null)
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room is null)
            {
                boxedData = null;
                return false;
            }
            props = room.CustomProperties;
        }

        if (!props.TryGetValue(k_PropKeyCustomData, out boxedData) || boxedData is null)
            return false;

        if (props.TryGetValue(k_PropKeyVersion, out var box) && box is int version)
            isLocal = version == s_LastVersion;

        return true;
    }


    //
    // MonoBehaviourPunCallbacks impl.

    #region [Monobehaviour Methods]

    public override void OnEnable()
    {
        base.OnEnable();
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnDisable()
    {
        base.OnDisable();

        s_OnRoomDataUpdated = LogOnRoomDataUpdated;
        s_OnCustomDataUpdated = LogOnCustomDataUpdated;

        OnApplicationQuit();
    }

    IEnumerator OnApplicationPause(bool pause)
    {
        if (pause)
        {
            StopAllCoroutines();
            yield break;
        }

        yield return null;

        while (Application.internetReachability == NetworkReachability.NotReachable)
            yield return null;

        // get the "low-level" peer state since it's what's checked before we try [re]connecting to Photon:
        var peerState = PhotonNetwork.NetworkingClient.LoadBalancingPeer.PeerState;
        Sampleton.Log($">> PhotonNetwork is {peerState}... (frame={Time.frameCount})");

        var ui = Sampleton.BaseUI;

        if (PhotonNetwork.InRoom)
        {
            if (ui)
                ui.DisplayRoomPanel();
            yield break;
        }

        if (ui)
            ui.DisplayLobbyPanel();

        if (PhotonNetwork.InLobby)
            yield break;

        if (peerState == PeerStateValue.Connecting)
            yield break;

        if (PhotonNetwork.ReconnectAndRejoin())
        {
            Sampleton.Log($">> PhotonNetwork.ReconnectAndRejoin()");
            yield break;
        }

        if (TryJoinLobby())
        {
            Sampleton.Log($">> PhotonNetwork.JoinLobby()");
            yield break;
        }

        if (PhotonNetwork.ConnectUsingSettings())
        {
            Sampleton.Log($">> PhotonNetwork.ConnectUsingSettings()");
            yield break;
        }

        yield return null;

        switch (PhotonNetwork.NetworkingClient.LoadBalancingPeer.PeerState)
        {
            case PeerStateValue.Disconnected:
            case PeerStateValue.Disconnecting:
                Sampleton.Error($">> ERR: PhotonNetwork failed to (re)connect after app resumed.");
                break;
        }
    }

    void OnApplicationQuit()
    {
        if (PhotonNetwork.InRoom)
        {
            // Call LeaveRoom(false) explicitly, so intentionally
            // leaving doesn't make your "inactive" slot linger:
            PhotonNetwork.LeaveRoom(becomeInactive: false);
        }

        PhotonNetwork.Disconnect();
    }

    #endregion [Monobehaviour Methods]


    #region [Photon Callbacks]

    public override void OnConnectedToMaster()
    {
        Sampleton.Log($"Photon::OnConnectedToMaster, CloudRegion='{PhotonNetwork.CloudRegion}'");

        TryJoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Sampleton.Log($"Photon::OnJoinedLobby: {PhotonNetwork.CurrentLobby}");

        if (Sampleton.BaseUI is LocalSpaceSharingUI ui)
        {
            ui.NotifyLobbyAvailable(true);
            ui.DisplayLobbyPanel();
        }
    }

    public override void OnLeftLobby()
    {
        Sampleton.Log($"Photon::OnLeftLobby");

        if (Sampleton.BaseUI is LocalSpaceSharingUI ui)
            ui.NotifyLobbyAvailable(false);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        if (Sampleton.BaseUI is LocalSpaceSharingUI ui)
        {
            ui.NotifyRoomListUpdate(null);
        }

        switch (cause)
        {
            case DisconnectCause.DisconnectByServerLogic:
            case DisconnectCause.DisconnectByDisconnectMessage:
            case DisconnectCause.DnsExceptionOnConnect:
            case DisconnectCause.ServerAddressInvalid:
            case DisconnectCause.InvalidRegion:
            case DisconnectCause.InvalidAuthentication:
            case DisconnectCause.AuthenticationTicketExpired:
            case DisconnectCause.CustomAuthenticationFailed:
            case DisconnectCause.OperationNotAllowedInCurrentState:
            case DisconnectCause.DisconnectByOperationLimit:
            case DisconnectCause.MaxCcuReached:
                Sampleton.Error($"Photon:OnDisconnected: {cause}\n- will NOT attempt to automatically ReconnectAndRejoin().");
                return;

            case DisconnectCause.Exception:
            case DisconnectCause.ExceptionOnConnect:
            case DisconnectCause.ClientTimeout:
            case DisconnectCause.ServerTimeout:
            case DisconnectCause.DisconnectByServerReasonUnknown:
                Sampleton.Warn($"Photon::OnDisconnected: {cause}\n+ attempting auto ReconnectAndRejoin() in 2 seconds...");
                Sampleton.DelayCall(2f, () => _ = PhotonNetwork.ReconnectAndRejoin() || PhotonNetwork.ConnectUsingSettings());
                return;

            default:
            case DisconnectCause.None:
            case DisconnectCause.DisconnectByClientLogic:
                Sampleton.Log($"Photon::OnDisconnected: {cause}");
                return;
        }
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        string room = s_LastRoomName;
        s_LastRoomName = null;

        if (!CheckConnection(tryReconnect: false, logOnFail: true))
            return;

        if (returnCode == ErrorCode.GameDoesNotExist && !string.IsNullOrEmpty(room) && TryJoinRoom(room))
        {
            Sampleton.Warn(
                $"Photon::OnJoinRoomFailed: \"{message}\" ({returnCode})\n" +
                $"+ Creating a new \"{room}\"..."
            );
            return;
        }

        Sampleton.Error($"Photon::OnJoinRoomFailed: \"{message}\" ({returnCode})");

        if (!PhotonNetwork.InLobby)
            TryJoinLobby();
        else if (Sampleton.GetActiveUI(out var ui))
            ui.DisplayLobbyPanel();
    }

    public override void OnJoinedRoom()
    {
        var room = PhotonNetwork.CurrentRoom;

        Sampleton.Log($"Photon::OnJoinedRoom: {room.Name}");

        s_LastRoomName = room.Name;
        s_LastVersion = -1; // rejoining should trigger On*DataUpdated even if the data was originally local

        OnRoomPropertiesUpdate(room.CustomProperties);

        if (Sampleton.GetActiveUI(out var ui))
            ui.DisplayRoomPanel();
    }

    public override void OnLeftRoom()
    {
        Sampleton.Log("Photon::OnLeftRoom"); // mainly for log consistency

        s_LastRoomName = null;
        s_LastVersion = -1;

        if (Sampleton.GetActiveUI(out var ui))
            ui.DisplayLobbyPanel();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Sampleton.Log($"Photon::OnPlayerEnteredRoom: {newPlayer}");

        if (Sampleton.BaseUI is LocalSpaceSharingUI ui)
            ui.NotifyRoomUsersUpdated();
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (otherPlayer.IsInactive)
        {
            Sampleton.Log($"Photon::OnPlayerLeftRoom: {otherPlayer} has gone inactive.");
            return;
        }

        Sampleton.Log($"Photon::OnPlayerLeftRoom: {otherPlayer} has left.");

        if (Sampleton.BaseUI is LocalSpaceSharingUI ui)
            ui.NotifyRoomUsersUpdated();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        if (Sampleton.BaseUI is LocalSpaceSharingUI ui)
            ui.NotifyRoomListUpdate(roomList);
    }

    public override void OnRoomPropertiesUpdate(Hashtable changedProps)
    {
        Assert.IsNotNull(s_OnRoomDataUpdated, "s_OnRoomDataUpdated should never be null");
        Assert.IsNotNull(s_OnCustomDataUpdated, "s_OnCustomDataUpdated should never be null");

        if (changedProps != PhotonNetwork.CurrentRoom.CustomProperties) // intentional reference comparison
            Sampleton.Log($"Photon::OnRoomPropertiesUpdate({string.Join(",", changedProps.Keys)})");

        if (TryGetRoomData(out var group, out var rooms, out var floorPose, out bool isLocal, props: changedProps))
        {
            s_OnRoomDataUpdated(group, rooms, floorPose, isLocal);
        }

        if (TryGetCustomData(out var boxedData, out isLocal, props: changedProps))
        {
            s_OnCustomDataUpdated(boxedData, isLocal);
        }
    }

    #endregion [Photon Callbacks]

}
