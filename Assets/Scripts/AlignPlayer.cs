// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;

using Meta.XR.MRUtilityKit;

using UnityEngine;
using UnityEngine.Events;


[System.Obsolete("As of v81, MRUK no longer allows clients to manually align MRUKAnchors.")]
[MetaCodeSample("SpaceSharing")]
public class AlignPlayer : MonoBehaviour
{
    [SerializeField]
    public UnityEvent<bool> m_OnAlign = new();

    public void AlignFloorAnchorToOrigin()
    {
        var mruk = MRUK.Instance;
        if (!mruk)
        {
            Sampleton.Error("- MRUK NOT FOUND");
            m_OnAlign.Invoke(false);
            return;
        }
        var currentRoom = mruk.GetCurrentRoom();
        var rooms = mruk.Rooms;
        if (rooms.Count == 0 || !currentRoom)
        {
            Sampleton.Error("- MRUK ROOMS NOT FOUND");
            m_OnAlign.Invoke(false);
            return;
        }
        if (!currentRoom.FloorAnchor)
        {
            Sampleton.Error("- FLOOR ANCHOR NOT FOUND");
            m_OnAlign.Invoke(false);
            return;
        }

        var floorAnchor = currentRoom.FloorAnchor.transform;

        // We're adjusting all anchored Room Transforms such that the current Room's "Floor" anchor aligns with /
        // becomes Unity's world origin (0,0,0).

        // Calculate the offset for that adjustment:

        var floorOffset = new Pose(
            position: floorAnchor.position,
        //  Floor anchors are oriented Z-up/Y-forward ~ we should flip it:
            rotation: Quaternion.LookRotation(forward: floorAnchor.up, upwards: Vector3.up)
        );

        // Aside:
        // ( We use Vector3.up instead of floorAnchor.forward:  Scene anchors only orient themselves yaw-wise relative
        //   to their Room, so to avoid introducing any tilt noise, we can "fudge it" and assume as if the current floor
        //   anchor's up vector is and has always been the Y(+)-axis. This assumption would only ever be minutely off,
        //   and unlike the alternative (floorAnchor.forward) would not accumulate error with successive alignments. )

        // We now have the offset that would transform (move) an object at (0,0,0) to match the floorAnchor's pose,
        // but what we want is the offset that does the OPPOSITE.

        // This is calculated by "inverting" the Pose:
        floorOffset.rotation = Quaternion.Inverse(floorOffset.rotation);
        floorOffset.position = floorOffset.rotation * -floorOffset.position;

        // "Apply" this inverse offset to each Room (including the current):
        foreach (var room in rooms)
        {
            // AKA put each room into the child space of the (inverted) floor pose
            room.transform.SetPositionAndRotation(
                floorOffset.position + floorOffset.rotation * room.transform.position,
                floorOffset.rotation * room.transform.rotation
            );
            // ( This is akin to affine Matrix4x4 multiplication, which is what powers parent-child relationships in
            //   Unity's hierarchy.  However, due to Unity's "lazy-recalculation" model for Transform matrices,
            //   matrix products cannot be set directly.    (It is probably for the best!)
            //   This thing we often do instead (above) is sometimes called a "Pose product" / "Pose multiplication",
            //   but terms vary widely. )
        }

        m_OnAlign.Invoke(true);
    }

}
