using System;
using Sim;
using UnityEngine;

// ─── ApartmentRoomState ───────────────────────────────────────────────────────
// Payload attaché à la room "apt:{street}:{door}" via ServerPropManager.SetRoomState.
// Sérialisé/désérialisé avec JsonUtility (cohérent avec le reste du projet).
// Utilise Sim.CoverData (Save/SceneData.cs) pour walls/grounds.

[Serializable]
public class ApartmentRoomState
{
    public string street;
    public int doorNumber;
    public string tenantId;
    public string tenantFullName;
    public string presetName;
    public CoverData[] walls;
    public CoverData[] grounds;

    public byte[] Serialize() => System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));

    public static ApartmentRoomState Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0) return new ApartmentRoomState();
        return JsonUtility.FromJson<ApartmentRoomState>(System.Text.Encoding.UTF8.GetString(data))
               ?? new ApartmentRoomState();
    }
}

// ─── CoverDataWrapper ─────────────────────────────────────────────────────────
// Wrapper pour sérialiser Sim.CoverData[] via JsonUtility (ne supporte pas les
// tableaux nus). Utilisé dans C2S_ApplyWallCovers / C2S_ApplyGroundCovers.

[Serializable]
public class CoverDataWrapper
{
    public CoverData[] items;

    public byte[] Serialize() => System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));

    public static CoverData[] Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0) return Array.Empty<CoverData>();
        CoverDataWrapper w = JsonUtility.FromJson<CoverDataWrapper>(System.Text.Encoding.UTF8.GetString(data));
        return w?.items ?? Array.Empty<CoverData>();
    }
}