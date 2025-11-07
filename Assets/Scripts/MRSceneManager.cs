// Copyright (c) Meta Platforms, Inc. and affiliates.

using JetBrains.Annotations;

using Meta.XR.MRUtilityKit;

using Photon.Pun;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;


/// <summary>
///   Mixed Reality Scene Manager - handles loading, scanning, sharing, etc of MRUKRooms (often referred to as "scenes")
///   in the local mixed reality space.
/// </summary>
public class MRSceneManager : MonoBehaviour
{
    //
    // Static interface

    public static void LoadSharedScene()
    {
        if (AssertInstance())
            s_Instance.LoadSharedSceneImpl();
    }

    public static void LoadOrScanLocalScene()
    {
        if (AssertInstance())
            s_Instance.LoadOrScanLocalSceneImpl();
    }

    public static void ShareLocalScene()
    {
        if (AssertInstance())
            s_Instance.ShareLocalSceneImpl();
    }


    public static void SetSharedSceneUuids([CanBeNull] ICollection<Guid> sharedRoomUuids, Guid groupUuid)
    {
        if (AssertInstance())
            s_Instance.SetSharedSceneUuidsImpl(sharedRoomUuids ?? Array.Empty<Guid>(), groupUuid);
    }

    public static void SetHostAlignment((Guid roomUuid, Pose pose)? alignment)
    {
        if (AssertInstance())
            s_Instance.m_HostAlignment = alignment;
    }


    public static void Clear()
    {
        if (MRUK.Instance)
        {
            int nRooms = MRUK.Instance.Rooms.Count;
            if (nRooms > 0)
            {
                MRUK.Instance.ClearScene();
                Sampleton.Log($"<i>* Unloaded {nRooms} MRUKRooms *</i>");
            }
        }
        if (s_Instance)
        {
            s_Instance.m_SharedGroupId = null;
            s_Instance.m_SharedRoomIds = null;
            s_Instance.m_HostAlignment = null;
        }
    }


    //
    // Instance interface

    public void ToggleGlobalMesh()
    {
        if (!MRUK.Instance)
        {
            Sampleton.Error($"{nameof(ToggleGlobalMesh)} FAILED! (no MRUK instance)");
            return;
        }

        if (MRUK.Instance.Rooms.Count == 0)
        {
            Sampleton.Error($"{nameof(ToggleGlobalMesh)} FAILED! (there are no rooms currently loaded)");
            return;
        }

        if (!m_GlobalMeshMaterial)
        {
            Sampleton.Error($"{nameof(ToggleGlobalMesh)} FAILED! (this sample scene does not provide a global mesh material)");
            return;
        }

        if (m_GlobalMesh)
        {
            m_GlobalMesh.HideMesh = !m_GlobalMesh.HideMesh;
            return;
        }

        m_GlobalMesh = new GameObject("_globalMeshViz", typeof(EffectMesh)).GetComponent<EffectMesh>();
        m_GlobalMesh.Labels = MRUKAnchor.SceneLabels.GLOBAL_MESH;
        m_GlobalMesh.MeshMaterial = m_GlobalMeshMaterial;

        m_GlobalMesh.HideMesh = false;
        m_GlobalMesh.CreateMesh();
    }


    //
    // private

    static MRSceneManager s_Instance;

    static bool AssertInstance()
    {
        Assert.IsNotNull(s_Instance, $"A {nameof(MRSceneManager)} instance must exist!");
        return s_Instance.isActiveAndEnabled;
    }

    //
    // Serialized fields

    [SerializeField]
    string m_HardcodedGroupUUID;
    [SerializeField]
    protected Material m_GlobalMeshMaterial;

    [Space]
    [SerializeField, Tooltip("OnSceneLoaded should update UI to reflect that room anchors have been loaded (regardless of shared vs local). Registered with MRUK.RegisterSceneLoadedCallback.")]
    public UnityEvent OnSceneLoaded = new();
    [SerializeField, Tooltip("OnSceneShared should update UI to reflect that room anchors have been shared to the current Photon Room or colocation, and should be loadable.")]
    public UnityEvent OnSceneShared = new();

    // Runtime fields

    Guid? m_SharedGroupId;
    ICollection<Guid> m_SharedRoomIds = Array.Empty<Guid>();
    (Guid alignmentRoomUuid, Pose floorWorldPoseOnHost)? m_HostAlignment;

    EffectMesh m_GlobalMesh;


    //
    // MonoBehaviour messages

    void OnEnable()
    {
        Assert.IsNull(s_Instance, $"{nameof(MRSceneManager)}.s_Instance");
        s_Instance = this;
    }

    void OnDisable()
    {
        if (s_Instance == this)
            s_Instance = null;
        if (Sampleton.GetActiveUI(out var ui))
        {
            ui.OnDisplayLobby -= Clear;
        }
    }

    IEnumerator Start()
    {
        yield return null;

        if (!MRUK.Instance)
        {
            Sampleton.Error($"{nameof(MRSceneManager)} can't do anything without an MRUK instance!");
            enabled = false;
            yield break;
        }

        MRUK.Instance.RegisterSceneLoadedCallback(OnSceneLoaded.Invoke);

        if (Sampleton.GetActiveUI(out var ui))
        {
            ui.OnDisplayLobby += Clear;
        }
    }

    //
    // instance impl.

    Guid GetSharedGroupId()
    {
        if (!m_SharedGroupId.HasValue)
        {
            if (m_HardcodedGroupUUID == "build")
            {
                m_SharedGroupId = Guid.Parse(Application.buildGUID);
                Sampleton.Log($"* Using buildGUID as Group Sharing UUID: {m_SharedGroupId}");
            }
            else if (Guid.TryParse(m_HardcodedGroupUUID, out var parsed))
            {
                m_SharedGroupId = parsed;
                Sampleton.Log($"* HARDCODED Group Sharing UUID parsed: {m_SharedGroupId}");
            }
            else
            {
                m_SharedGroupId = Guid.NewGuid();
                Sampleton.Log($"* NEW Group Sharing UUID generated: {m_SharedGroupId}");
            }
        }

        return m_SharedGroupId.Value;
    }

    bool IsReadyToShare(out MRUK mruk, out MRUKRoom curRoom, bool printReason = true)
    {
        mruk = MRUK.Instance;
        curRoom = null;

        if (!mruk || !mruk.IsInitialized)
        {
            if (printReason)
                Sampleton.Error("- Can't share - no MRUK GameObject in scene, or it has not been initialized.");
            return false;
        }

        curRoom = mruk.GetCurrentRoom();
        if (!curRoom)
        {
            if (printReason)
                Sampleton.Error("- Can't share - there are no (loaded) MRUKRooms to share.");
            return false;
        }

        switch (Sampleton.ConnectMethod)
        {
            case ConnectMethod.Photon:
                if (!Sampleton.PhotonRoomManager)
                {
                    if (printReason)
                        Sampleton.Error("- Can't share - no PhotonRoomManager in scene.");
                    return false;
                }

                if (!PhotonNetwork.IsConnected)
                {
                    if (printReason)
                        Sampleton.Error("- Can't share - not connected to the Photon network");
                    return false;
                }

                if (!PhotonNetwork.InRoom)
                {
                    if (printReason)
                        Sampleton.Error("- Can't share - not currently in a Photon Room.");
                    return false;
                }

                if (PhotonNetwork.CurrentRoom.PlayerCount == 0) // almost certainly dead code, but w/e
                {
                    if (printReason)
                        Sampleton.Error("- Can't share - no users to share with or can't get user list from Photon");
                    return false;
                }
                break;

            case ConnectMethod.ColocationSession:
                break;
        }

        return true;
    }

    bool IsReadyForLoad(out Guid groupUuid, out ICollection<Guid> roomIds, bool printReason = true)
    {
        groupUuid = m_SharedGroupId.GetValueOrDefault();
        roomIds = m_SharedRoomIds;

        if (groupUuid == Guid.Empty)
        {
            if (printReason)
                Sampleton.Error("Group Sharing UUID not yet shared/received.");
            return false;
        }

        Sampleton.Log("Group Sharing UUID: " + m_SharedGroupId);
        return true;
    }

    async void LoadSharedSceneImpl()
    {
        if (!IsReadyForLoad(out var groupUuid, out var roomIds))
            return;

        if (roomIds.Count == 0) // indicates "load everything idc"
            Sampleton.Log($"Loading all rooms shared with group {groupUuid}...");
        else if (roomIds.Count == 1)
            Sampleton.Log($"Loading 1 room shared with group {groupUuid}...");
        else
            Sampleton.Log($"Loading {roomIds.Count} rooms shared with group {groupUuid}...");

        foreach (var id in roomIds)
        {
            Sampleton.Log($"+ room: {id}");
        }

        var result = await MRUK.Instance.LoadSceneFromSharedRooms(
            roomIds,
            groupUuid,
            alignmentData: m_HostAlignment,
            removeMissingRooms: true
        );

        Sampleton.Log(
            $"... MRUK.LoadSceneFromSharedRooms: {result} ({(int)result})",
            result == 0 ? LogType.Log : result > 0 ? LogType.Warning : LogType.Error
        );
    }

    async void LoadOrScanLocalSceneImpl()
    {
        if (Sampleton.PlayerFace.IsInLoadedRoom())
        {
            Sampleton.Warn($"{nameof(LoadOrScanLocalScene)} won't be called while already in a loaded scene.");
            return;
        }

        Sampleton.Log($"{nameof(LoadOrScanLocalScene)}:");

        // always request a scan if rooms have already been loaded but our face isn't in any of them:
        if (MRUK.Instance.Rooms.Count > 0 && !await OVRScene.RequestSpaceSetup())
        {
            Sampleton.Error($"{nameof(OVRScene.RequestSpaceSetup)} FAILED! (not cancelled)");
            return;
        }

        var result = await MRUK.Instance.LoadSceneFromDevice(
            requestSceneCaptureIfNoDataFound: true,
            removeMissingRooms: true
        );

        Sampleton.Log(
            $"MRUK.LoadSceneFromDevice: {result} ({(int)result})",
            result == 0 ? LogType.Log : result > 0 ? LogType.Warning : LogType.Error
        );
    }

    async void ShareLocalSceneImpl()
    {
        if (!IsReadyToShare(out var mruk, out var curRoom, printReason: true))
            return;

        // TODO if scene ownership is mixed and shared to the same group UUID, this probably doesn't work as-is.

        var groupId = GetSharedGroupId();
        var task = await mruk.ShareRoomsAsync(mruk.Rooms, groupId);
        if (!task.Success)
        {
            Sampleton.Error($"{nameof(ShareLocalScene)} FAILED: {task.Status}({(int)task.Status})");
            return;
        }

        Sampleton.Log(
            $"{nameof(ShareLocalScene)}: {task.Status}({(int)task.Status})\n" +
            $"to group: {groupId}"
        );

        var roomIds = mruk.Rooms.Select(room => room.Anchor.Uuid).ToArray();
        for (int i = 0; i < roomIds.Length; ++i)
        {
            var uuid = roomIds[i];
            if (uuid == curRoom.Anchor.Uuid)
            {
                Sampleton.Log($"+ current room: {uuid}");
                // our Photon impl. wants the first room id to be the current room the host is in:
                (roomIds[0], roomIds[i]) = (roomIds[i], roomIds[0]);
            }
            else
            {
                Sampleton.Log($"+ side room: {uuid}");
            }
        }

        var floor = curRoom.FloorAnchor.transform;
        var floorPose = new Pose(floor.position, floor.rotation);

        SetHostAlignment((curRoom.Anchor.Uuid, floorPose));

        if (Sampleton.ConnectMethod == ConnectMethod.Photon)
        {
            PhotonRoomManager.PublishRoomData(groupId, roomIds, floorPose);
        }

        SetSharedSceneUuidsImpl(roomIds, groupId);
    }

    void SetSharedSceneUuidsImpl(ICollection<Guid> sharedRoomUuids, Guid groupUuid)
    {
        if (m_SharedRoomIds?.Count > 0 && m_SharedGroupId == groupUuid)
        {
            if (m_SharedRoomIds is not HashSet<Guid> allGroupRooms)
                allGroupRooms = new HashSet<Guid>(m_SharedRoomIds);
            allGroupRooms.UnionWith(sharedRoomUuids);
            m_SharedRoomIds = allGroupRooms;
        }
        else
        {
            m_SharedRoomIds = sharedRoomUuids;
        }

        m_SharedGroupId = groupUuid;

        OnSceneShared.Invoke();
    }

}
