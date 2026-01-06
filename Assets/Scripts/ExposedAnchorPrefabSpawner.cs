// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.Samples;

using Meta.XR.MRUtilityKit;

/// <summary>
///   This shallow class is necessitated by the private/protected nature of many useful fixtures inside MRUK's
///   AnchorPrefabSpawner component. This is a disgruntled workaround to expose said fixtures, allowing other scripts
///   and UI callbacks alike to call into this previously-sequestered functionality.
/// </summary>
/// <remarks>
///   Sealed = the madness ends here.
/// </remarks>
[MetaCodeSample("SpaceSharing")]
public sealed class ExposedAnchorPrefabSpawner : AnchorPrefabSpawner
{

    public void ClearSpawnees()
    {
        base.ClearPrefabs();
    }

    public void SpawnPrefabs()
    {
        base.SpawnPrefabs(clearPrefabs: true);
    }

    public void SetSpawneesActive(bool active)
    {
        foreach (var spawnee in AnchorPrefabSpawnerObjects.Values)
        {
            spawnee.SetActive(active);
        }
    }

}
