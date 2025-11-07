// Copyright (c) Meta Platforms, Inc. and affiliates.

using JetBrains.Annotations;

using Meta.XR.MRUtilityKit;

using System;
using System.Text;

using UnityEngine;


static class SampleExtensions
{

    public static readonly Encoding EncodingForSerialization
        = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);


    public static bool IsInLoadedRoom(in this Vector3 worldPosition, bool testY = false)
    {
        if (!MRUK.Instance)
            return false;

        var room = MRUK.Instance.GetCurrentRoom();
        if (!room)
            return false;

        return room.IsPositionInRoom(worldPosition, testY);
    }

    public static bool IsInLoadedRoom([CanBeNull] this Transform transform, bool testY = false)
    {
        return transform && IsInLoadedRoom(transform.position, testY);
    }


    public static string SafeTypeName(this object box)
    {
        if (box is null)
            return "null";

        if (box is not Type type)
            return SafeTypeName(box.GetType());

        if (type.IsArray)
            return $"{SafeTypeName(type.GetElementType())}[]";

        // note: the following sugar is not exhaustive,
        //       as this utility is only used on a known subset of input types anyway.
        return Type.GetTypeCode(type) switch
        {
            TypeCode.String => "string",
            TypeCode.Single => "float",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "long",
            TypeCode.UInt64 => "ulong",
            TypeCode.Byte => "byte",
            _ => type.Name
        };
    }


    public static string Brief(in this Guid guid)
        => $"{guid.ToString("N").Remove(8)}[..]";


    public static string ForLogging(this MRUK.LoadDeviceResult status)
        => $"{status}({(int)status})"; // MRUK overrides OVRPlugin.Result values with other meanings

    public static string ForLogging(this OVRSpatialAnchor.OperationResult status, bool details = true)
        => StatusForLogging(status, details);

    public static string ForLogging(this OVRColocationSession.Result status, bool details = true)
        => StatusForLogging(status, details);

    public static string ForLogging(this OVRAnchor.EraseResult status, bool details = true)
        => StatusForLogging(status, details);

    public static string ForLogging(this OVRAnchor.SaveResult status, bool details = true)
        => StatusForLogging(status, details);

    public static string ForLogging(this OVRAnchor.ShareResult status, bool details = true)
        => StatusForLogging(status, details);

    public static string ForLogging(this OVRAnchor.FetchResult status, bool details = true)
        => StatusForLogging(status, details);

    public static string StatusForLogging<T>(T status, bool details) where T : struct, Enum
        => details ? $"{(OVRPlugin.Result)(object)status}({(int)(object)status}){StatusExtraDetails(status)}"
                   : $"{(OVRPlugin.Result)(object)status}({(int)(object)status})";

    public static string StatusExtraDetails<T>(T status) where T : struct, Enum
    {
        switch ((OVRPlugin.Result)(object)status)
        {
            case OVRPlugin.Result.Success:
                break;

            case OVRPlugin.Result.Failure_SpaceCloudStorageDisabled:
                const string kEnhancedSpatialServicesInfoURL = "https://www.meta.com/help/quest/articles/in-vr-experiences/oculus-features/point-cloud/";
#if UNITY_EDITOR
                if (UnityEditor.SessionState.GetBool(kEnhancedSpatialServicesInfoURL, true))
                {
                    UnityEditor.SessionState.SetBool(kEnhancedSpatialServicesInfoURL, false);
#else
                if (Debug.isDebugBuild)
                {
#endif
                    Sampleton.Log($"  -> Application.OpenURL(\"{kEnhancedSpatialServicesInfoURL}\")");
                    Application.OpenURL(kEnhancedSpatialServicesInfoURL);
                }
                return "\nEnhanced Spatial Services is disabled on your device. Enable it in OS Settings > Privacy & Safety > Device Permissions";

            case OVRPlugin.Result.Failure_SpaceGroupNotFound:
                return "\n(this is expected if anchors have not been shared to this group UUID yet)";

            case OVRPlugin.Result.Failure_ColocationSessionNetworkFailed:
            case OVRPlugin.Result.Failure_SpaceNetworkTimeout:
            case OVRPlugin.Result.Failure_SpaceNetworkRequestFailed:
                if (Application.internetReachability == NetworkReachability.NotReachable)
                    return "\n(device lacks internet connection)";
                else
                    return "\n(device has internet)";
        }

        return string.Empty;
    }

} // end static class SampleExtensions
