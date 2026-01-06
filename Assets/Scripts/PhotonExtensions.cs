// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;

using ExitGames.Client.Photon;

using Photon.Pun;

using System;

using UnityEngine;


using MemoryMarshal = System.Runtime.InteropServices.MemoryMarshal;


[MetaCodeSample("SpaceSharing")]
public static class PhotonExtensions
{

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RegisterAdditionalTypeSerialization()
    {
        const int kPose3Size = 7 * sizeof(float); // sizeof(Vector3) + sizeof(Quaternion)

        Sampleton.Log($"AfterSceneLoad: {nameof(PhotonExtensions)}::{nameof(RegisterAdditionalTypeSerialization)}");

        if (!Protocol.TryRegisterType(typeof(Guid), (byte)'G', guidWrite, guidRead))
            Sampleton.Error($"Photon ERR: failed to register {nameof(Guid)} serde");

        // note: capital 'P' was taken by Photon's own CustomTypes.cs, and it only won the race in Editor PlayMode.
        if (!Protocol.TryRegisterType(typeof(Pose), (byte)'p', pose3Write, pose3Read))
            Sampleton.Error($"Photon ERR: failed to register {nameof(Pose)} serde");

        // default fallback username
        PhotonNetwork.NickName = $"Anon{UnityEngine.Random.Range(0, 10000):0000}";

        return;

        static byte[] guidWrite(object box)
        {
            return box is Guid guid ? guid.ToByteArray() : new byte[16];
        }

        static object guidRead(byte[] bytes)
        {
            return bytes?.Length == 16 ? new Guid(bytes) : Guid.Empty;
        }

        static bool isValid(in Pose p)
        {
            var t = p.position;
            return (t.x == 0f || float.IsNormal(t.x)) &&
                   (t.y == 0f || float.IsNormal(t.y)) &&
                   (t.z == 0f || float.IsNormal(t.z)) &&
                   Quaternion.Dot(p.rotation, p.rotation) is > 1f - Vector4.kEpsilon and < 1f + Vector4.kEpsilon;
        }

        static byte[] pose3Write(object box)
        {
            var bytes = new byte[kPose3Size];
            if (box is not Pose p || !isValid(p))
                p = Pose.identity;
            MemoryMarshal.Write(bytes, ref p);
            return bytes;
        }

        static object pose3Read(byte[] bytes)
        {
            if (bytes?.Length != kPose3Size)
                return Pose.identity;
            var p = MemoryMarshal.Read<Pose>(bytes);
            return isValid(p) ? p : Pose.identity;
        }
    }

} // end static class PhotonExtensions
