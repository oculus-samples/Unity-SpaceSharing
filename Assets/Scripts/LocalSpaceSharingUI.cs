// Copyright (c) Meta Platforms, Inc. and affiliates.

using Photon.Pun;
using Photon.Realtime;

using System.Collections;
using System.Collections.Generic;
using System.Text;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

using TMPro;

using Guid = System.Guid;


public class LocalSpaceSharingUI : BaseUI
{
    //
    // Serialized fields

    [Header(nameof(LocalSpaceSharingUI))]

    [SerializeField]
    bool m_AutoLoadRoomsWhenPossible = true;

    [SerializeField, Tooltip(
        "If disabled, \"(inactive)\" Photon Players will not " +
        "appear in the current Room's \"Users:\" list.\n\n" +
        "If enabled, the per-Room \"(inactive)\" status and CustomProperties " +
        "for each Photon Player will be printed in the list.\n\n" +
        "Note: \"(inactive)\" Players are not considered in the Room anymore; " +
        "their slot remains reserved for up to a minute in case they return.")]
    bool m_VerboseUserList;

    [SerializeField, Range(0f, 8f)]
    float m_UIUpdateInterval = 1f;

    [SerializeField]
    Button m_CreateRoomBtn;

    [SerializeField]
    Button m_FindRoomsBtn;

    [SerializeField]
    GameObject m_RoomListPanel;

    [SerializeField]
    GameObject m_RoomListItemTemplate;

    [SerializeField]
    TMP_Text m_StatusText;

    [SerializeField]
    TMP_Text m_RoomText;

    [SerializeField]
    TMP_Text m_UserText;

    // runtime fields

    static readonly StringBuilder s_TextBuf = new();


    //
    // UI UnityEvent listeners

    // in lobby:

    public void OnCreateRoomBtn()
    {
        Assert.AreEqual(ConnectMethod.Photon, Sampleton.ConnectMethod, "Sampleton.ConnectMethod should be Photon");

        Sampleton.Log($"{nameof(OnCreateRoomBtn)}:");

        if (!PhotonRoomManager.CheckConnection(warn: true))
            return;

        string username = Sampleton.GetNickname();
        string newRoomName = $"{username}'s room";

        Sampleton.Log($"+ Joining or creating \"{newRoomName}\" ...");

        if (PhotonRoomManager.TryJoinRoom(newRoomName))
            return;

        Sampleton.Error($"ERR: Room creation request not sent to server!");
    }

    public void OnFindRoomsBtn()
    {
        Assert.AreEqual(ConnectMethod.Photon, Sampleton.ConnectMethod, "Sampleton.ConnectMethod should be Photon");

        Sampleton.Log(nameof(OnFindRoomsBtn));

        if (!PhotonRoomManager.CheckConnection(warn: true))
            return;

        m_RoomListPanel.SetActive(true);
    }

    public void OnJoinRoomBtn(TMP_Text roomName)
    {
        Assert.AreEqual(ConnectMethod.Photon, Sampleton.ConnectMethod, "Sampleton.ConnectMethod should be Photon");

        Sampleton.Log($"{nameof(OnJoinRoomBtn)}: \"{roomName.text}\"");

        if (PhotonRoomManager.TryJoinRoom(roomName.text))
            return;

        Sampleton.Error($"ERR: Photon failed to join \"{roomName.text}\".");
    }

    // in room:

    public void OnLoadLocalSceneButtonPressed()
    {
        Sampleton.Log(nameof(OnLoadLocalSceneButtonPressed));
        MRSceneManager.LoadOrScanLocalScene();
    }

    public void OnLoadSharedSceneButtonPressed()
    {
        Sampleton.Log(nameof(OnLoadSharedSceneButtonPressed));
        MRSceneManager.LoadSharedScene();
    }

    public void OnShareLocalSceneButtonPressed()
    {
        Sampleton.Log(nameof(OnShareLocalSceneButtonPressed));
        MRSceneManager.ShareLocalScene();
    }


    //
    // C# Public Interface

    public void NotifyLobbyAvailable(bool connected)
    {
        if (m_CreateRoomBtn)
            m_CreateRoomBtn.interactable = connected;

        if (m_FindRoomsBtn)
            m_FindRoomsBtn.interactable = connected;

        if (!connected)
            NotifyRoomListUpdate(null);
    }

    public void NotifyRoomListUpdate(List<RoomInfo> roomList)
    {
        foreach (Transform roomTransform in m_RoomListPanel.transform)
        {
            if (roomTransform.gameObject != m_RoomListItemTemplate)
                Destroy(roomTransform.gameObject);
        }

        if (roomList is null || roomList.Count == 0)
            return;

        foreach (var room in roomList)
        {
            var entry = Instantiate(m_RoomListItemTemplate, m_RoomListPanel.transform);
            entry.GetComponentInChildren<TMP_Text>().text = room.Name;
            entry.SetActive(true);
        }
    }

    public void NotifyRoomUsersUpdated()
    {
        UpdateUserList();
    }


    //
    // MonoBehaviour messages & overrides

    protected virtual void OnEnable()
    {
        OnDisplayRoom += UpdateRoomName;
        OnDisplayLobby += UpdateLobbyInteractability;

        PhotonRoomManager.OnRoomDataUpdated += OnPhotonRoomUpdated;
    }

    protected virtual void OnDisable()
    {
        OnDisplayRoom -= UpdateRoomName;
        OnDisplayLobby -= UpdateLobbyInteractability;

        PhotonRoomManager.OnRoomDataUpdated -= OnPhotonRoomUpdated;
    }

    protected override IEnumerator Start()
    {
        yield return base.Start();

        if (m_RoomListItemTemplate.scene == gameObject.scene) // (not a prefab ref)
        {
            m_RoomListItemTemplate.SetActive(false);
        }

        m_RoomListPanel.SetActive(false);

        switch (Sampleton.ConnectMethod)
        {
            case ConnectMethod.Photon:
                _ = StartCoroutine(UpdatePhotonUI(new WaitForSecondsRealtime(m_UIUpdateInterval)));
                break;
        }
    }


    //
    // Photon-centric impl.

    void OnPhotonRoomUpdated(Guid groupId, Guid[] roomIds, Pose? floorPose, bool isLocal)
    {
        if (isLocal)
        {
            Sampleton.Log(
                $"{nameof(OnPhotonRoomUpdated)}: NO-OP: No need to load your own room twice ({nameof(isLocal)})"
            );
            return;
        }

        bool tryAutoLoadRooms = m_AutoLoadRoomsWhenPossible;

        Sampleton.Log(
            $"{nameof(OnPhotonRoomUpdated)}({nameof(tryAutoLoadRooms)} = {tryAutoLoadRooms})"
        );

        MRSceneManager.SetSharedSceneUuids(roomIds, groupId);

        MRSceneManager.SetHostAlignment(floorPose.HasValue ? (roomIds[0], floorPose.Value) : null);

        if (!tryAutoLoadRooms)
            return;

        if (Sampleton.PlayerFace.IsInLoadedRoom())
        {
            Sampleton.Log(
                "-> Because you are already in a loaded room, the shared scene won't auto-load." +
                " You can attempt to load it manually using the \"Load Shared\" button."
            );
        }
        else
        {
            MRSceneManager.LoadSharedScene();
        }
    }


    IEnumerator UpdatePhotonUI(object interval)
    {
        bool expectReachable = true;
        while (this)
        {
            if (ShowingLobbyPanel)
            {
                UpdateLobbyInteractability();
            }

            if (ShowingRoomPanel)
            {
                UpdateRoomName();
                UpdateStatus(ref expectReachable);
                UpdateUserList();
            }

            yield return interval;
        }
    }

    void UpdateLobbyInteractability()
    {
        switch (Sampleton.ConnectMethod)
        {
            case ConnectMethod.Photon:
                NotifyLobbyAvailable(PhotonNetwork.InLobby);
                break;
            default:
                NotifyLobbyAvailable(false);
                break;
        }
    }

    void UpdateRoomName()
    {
        s_TextBuf.Clear();
        s_TextBuf.Append("Room: ");
        s_TextBuf.Append(PhotonRoomManager.CurrentRoomName);
        m_RoomText.SetText(s_TextBuf);
    }

    void UpdateStatus(ref bool expectReachable)
    {
        s_TextBuf.Clear();
        s_TextBuf.Append("Status: ");
        s_TextBuf.Append(PhotonNetwork.NetworkClientState);

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            s_TextBuf.Append("\n<color=red>- NetworkReachability.NotReachable</color>");
            if (expectReachable)
            {
                Sampleton.Warn($"WARNING: Application.internetReachability == {Application.internetReachability}");
                expectReachable = false;
            }
        }
        else
        {
            if (!expectReachable)
            {
                Sampleton.Log($"Internet reachability restored! ({Application.internetReachability})");
                expectReachable = true;
            }
        }

        m_StatusText.SetText(s_TextBuf);
    }

    void UpdateUserList()
    {
        var players = new List<Player>(PhotonNetwork.PlayerList); // pre-sorted
        var deduper = new HashSet<string>();

        int i = players.Count;
        while (i-- > 0)
        {
            // exploit the fact that the bottom-most occurence will always be
            // the most up-to-date instance for the same username:
            if (!deduper.Add(players[i].NickName))
                players.RemoveAt(i);
        }

        s_TextBuf.Clear();
        s_TextBuf.Append("Users:");

        foreach (var player in players)
        {
            if (m_VerboseUserList)
                s_TextBuf.Append("\n- ").Append(player.ToStringFull());
            else if (!player.IsInactive)
                s_TextBuf.Append("\n- ").Append(player);
        }

        m_UserText.SetText(s_TextBuf);
    }

}
