// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;

using Meta.XR.MRUtilityKit;

using System.Collections;

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

[MetaCodeSample("SpaceSharing")]
public class BaseUI : MonoBehaviour
{
    //
    // Instance interface

    public event UnityAction OnDisplayLobby
    {
        add
        {
            if (value is null)
                return;
            m_OnDisplayLobby.AddListener(value);
            if (ShowingLobbyPanel)
                value();
        }
        remove => m_OnDisplayLobby.RemoveListener(value);
    }

    public event UnityAction OnDisplayRoom
    {
        add
        {
            if (value is null)
                return;
            m_OnDisplayRoom.AddListener(value);
            if (ShowingRoomPanel)
                value();
        }
        remove => m_OnDisplayRoom.RemoveListener(value);
    }

    public bool ShowingRoomPanel => m_RoomPanel && m_RoomPanel.activeSelf;
    public bool ShowingLobbyPanel => m_LobbyPanel && m_LobbyPanel.activeSelf;


    public void DisplayRoomPanel()
    {
        if (!m_RoomPanel)
            return;

        if (m_LobbyPanel)
            m_LobbyPanel.SetActive(false);

        bool wasActive = m_RoomPanel.activeSelf;
        m_RoomPanel.SetActive(true);
        if (!wasActive)
            m_OnDisplayRoom.Invoke();
    }

    public void DisplayLobbyPanel()
    {
        if (!m_LobbyPanel)
            return;

        if (m_RoomPanel)
            m_RoomPanel.SetActive(false);

        bool wasActive = m_LobbyPanel.activeSelf;
        m_LobbyPanel.SetActive(true);
        if (!wasActive)
            m_OnDisplayLobby.Invoke();
    }


    //
    // Serialized fields

    [Header(nameof(BaseUI) + " (base)")]
    [FormerlySerializedAs("referencePoint")]
    [SerializeField]
    protected Transform m_MenuAnchor;

    [FormerlySerializedAs("menuPanel")]
    [SerializeField]
    protected GameObject m_RoomPanel;

    [FormerlySerializedAs("lobbyPanel")]
    [SerializeField]
    protected GameObject m_LobbyPanel;

    [Space]
    [SerializeField]
    protected UnityEvent m_OnDisplayLobby = new();
    [SerializeField]
    protected UnityEvent m_OnDisplayRoom = new();


    //
    // MonoBehaviour messages

    protected virtual void OnValidate()
    {
        if (!m_MenuAnchor)
        {
            var find = GameObject.Find("Ref Point");
            if (find)
                m_MenuAnchor = find.transform;
        }

        if (gameObject.scene.IsValid() && !m_MenuAnchor) // avoids erroring in prefab view
        {
            Debug.LogError($"\"{name}\" seems to be improperly set-up. (no anchor for canvas)", this);
        }
    }

    protected virtual IEnumerator Start()
    {
        transform.parent = m_MenuAnchor;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        DisplayLobbyPanel();

        // lazy state init for MRUK toggles
        var find = transform.FindChildRecursive("Toggle: MRUK World Locking");
        if (find && find.TryGetComponent<Toggle>(out var toggle))
        {
            if (MRUK.Instance)
            {
                toggle.interactable = true;
                toggle.SetIsOnWithoutNotify(MRUK.Instance.EnableWorldLock);
            }
            else
            {
                toggle.gameObject.SetActive(false);
            }
        }

        yield break;
    }

}
