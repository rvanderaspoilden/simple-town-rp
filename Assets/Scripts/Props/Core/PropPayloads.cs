using System;
using UnityEngine;

// ─── Header commun (présent dans TOUS les payloads) ───────────────────────────
//
// Bytes 0-4 de chaque payload :
//   [0]   isBuilt (0 = not built, 1 = built)
//   [1-4] presetId (int32 LE, -1 = pas de preset)
//
// Ce header est la migration directe des SyncVars `built` et `presetId`
// présents dans la classe de base Props. Chaque behaviour lit le header
// et délègue à PropsRenderer pour appliquer le style visuel.

public struct PropStateHeader {
    public bool IsBuilt;
    public int  PresetId;   // -1 = pas de preset appliqué

    public const int ByteSize = 5;

    public static PropStateHeader Default => new PropStateHeader { IsBuilt = true, PresetId = -1 };

    // ── Sérialisation ─────────────────────────────────────────────────────────

    public void WriteTo(byte[] buffer, int offset = 0) {
        buffer[offset]     = IsBuilt ? (byte)1 : (byte)0;
        BitConverter.GetBytes(PresetId).CopyTo(buffer, offset + 1);
    }

    public static PropStateHeader ReadFrom(byte[] data, int offset = 0) {
        if (data == null || data.Length < offset + ByteSize)
            return Default;
        return new PropStateHeader {
            IsBuilt  = data[offset] != 0,
            PresetId = BitConverter.ToInt32(data, offset + 1)
        };
    }
}

// ─── Generic (mobilier sans état interactif spécial) ──────────────────────────
// Payload : header seul (5 bytes)
// Exemples : table, étagère, lampe de sol

public struct GenericPropState {
    public PropStateHeader Header;

    public byte[] Serialize() {
        byte[] buf = new byte[PropStateHeader.ByteSize];
        Header.WriteTo(buf, 0);
        return buf;
    }

    public static GenericPropState Deserialize(byte[] data) =>
        new GenericPropState { Header = PropStateHeader.ReadFrom(data) };
}

// ─── Door ─────────────────────────────────────────────────────────────────────
// Payload : header(5) + isOpen(1) = 6 bytes

public struct DoorState {
    public PropStateHeader Header;
    public bool            IsOpen;

    public byte[] Serialize() {
        byte[] buf = new byte[PropStateHeader.ByteSize + 1];
        Header.WriteTo(buf, 0);
        buf[PropStateHeader.ByteSize] = IsOpen ? (byte)1 : (byte)0;
        return buf;
    }

    public static DoorState Deserialize(byte[] data) {
        int off = PropStateHeader.ByteSize;
        return new DoorState {
            Header = PropStateHeader.ReadFrom(data, 0),
            IsOpen = data != null && data.Length > off && data[off] != 0
        };
    }
}

// ─── Seat ─────────────────────────────────────────────────────────────────────
// Payload : header(5) + occupantNetId(4) = 9 bytes
// OccupantNetId == 0 → siège libre

public struct SeatState {
    public PropStateHeader Header;
    public uint            OccupantNetId;

    public bool IsOccupied => OccupantNetId != 0;

    public byte[] Serialize() {
        byte[] buf = new byte[PropStateHeader.ByteSize + 4];
        Header.WriteTo(buf, 0);
        BitConverter.GetBytes(OccupantNetId).CopyTo(buf, PropStateHeader.ByteSize);
        return buf;
    }

    public static SeatState Deserialize(byte[] data) {
        int off = PropStateHeader.ByteSize;
        return new SeatState {
            Header        = PropStateHeader.ReadFrom(data, 0),
            OccupantNetId = (data != null && data.Length >= off + 4)
                ? BitConverter.ToUInt32(data, off) : 0u
        };
    }
}

// ─── PaintBucket ──────────────────────────────────────────────────────────────
// Payload : header(5) + paintConfigId(4) + r(4) + g(4) + b(4) = 21 bytes
// Migration directe de PaintBucket.color (SyncVar Color) + paintConfigId (SyncVar int)

public struct PaintBucketState {
    public PropStateHeader Header;
    public int             PaintConfigId;
    public float           R, G, B;

    public Color Color => new Color(R, G, B);

    public byte[] Serialize() {
        byte[] buf = new byte[PropStateHeader.ByteSize + 16];
        Header.WriteTo(buf, 0);
        int off = PropStateHeader.ByteSize;
        BitConverter.GetBytes(PaintConfigId).CopyTo(buf, off); off += 4;
        BitConverter.GetBytes(R).CopyTo(buf, off);             off += 4;
        BitConverter.GetBytes(G).CopyTo(buf, off);             off += 4;
        BitConverter.GetBytes(B).CopyTo(buf, off);
        return buf;
    }

    public static PaintBucketState Deserialize(byte[] data) {
        int off = PropStateHeader.ByteSize;
        if (data == null || data.Length < off + 16)
            return new PaintBucketState { Header = PropStateHeader.Default };
        return new PaintBucketState {
            Header        = PropStateHeader.ReadFrom(data, 0),
            PaintConfigId = BitConverter.ToInt32 (data, off),
            R             = BitConverter.ToSingle(data, off + 4),
            G             = BitConverter.ToSingle(data, off + 8),
            B             = BitConverter.ToSingle(data, off + 12)
        };
    }
}

// ─── DeliveryBox ──────────────────────────────────────────────────────────────
// Payload : header(5) + deliveryCount(4) = 9 bytes
// Migration de DeliveryBox.deliveryCount (SyncVar uint)
// Le contenu des livraisons (Delivery[]) est récupéré via REST lors de l'ouverture
// et transmis par S2C_DeliveryBoxOpened — il n'est pas stocké dans le state.

public struct DeliveryBoxState {
    public PropStateHeader Header;
    public uint            DeliveryCount;

    public byte[] Serialize() {
        byte[] buf = new byte[PropStateHeader.ByteSize + 4];
        Header.WriteTo(buf, 0);
        BitConverter.GetBytes(DeliveryCount).CopyTo(buf, PropStateHeader.ByteSize);
        return buf;
    }

    public static DeliveryBoxState Deserialize(byte[] data) {
        int off = PropStateHeader.ByteSize;
        return new DeliveryBoxState {
            Header        = PropStateHeader.ReadFrom(data, 0),
            DeliveryCount = (data != null && data.Length >= off + 4)
                ? BitConverter.ToUInt32(data, off) : 0u
        };
    }
}

// ─── Dispenser ────────────────────────────────────────────────────────────────
// Payload : header seul (5 bytes)
// Le catalogue d'articles vient du ScriptableObject DispenserConfiguration,
// pas du state réseau. Seul le state visuel (built + preset) est synchronisé.

public struct DispenserState {
    public PropStateHeader Header;

    public byte[] Serialize() {
        byte[] buf = new byte[PropStateHeader.ByteSize];
        Header.WriteTo(buf, 0);
        return buf;
    }

    public static DispenserState Deserialize(byte[] data) =>
        new DispenserState { Header = PropStateHeader.ReadFrom(data) };
}

// ─── Payloads d'interaction C2S ───────────────────────────────────────────────

public static class SeatInteraction {
    public static readonly byte[] SitRequest    = { 0 };
    public static readonly byte[] RevokeRequest = { 1 };

    public static bool IsSitRequest   (byte[] data) => data != null && data.Length >= 1 && data[0] == 0;
    public static bool IsRevokeRequest(byte[] data) => data != null && data.Length >= 1 && data[0] == 1;
}

/// <summary>
/// Payload envoyé par le client pour acheter un article dans un Dispenser.
/// 4 bytes : itemId (int32 LE).
/// </summary>
public static class DispenserInteraction {
    public static byte[] BuyRequest(int itemId) => BitConverter.GetBytes(itemId);

    public static int GetItemId(byte[] data) =>
        (data != null && data.Length >= 4) ? BitConverter.ToInt32(data, 0) : -1;
}

/// <summary>
/// Payload envoyé par le client pour ouvrir sa boîte aux livraisons.
/// 1 byte : 0 = open.
/// </summary>
public static class DeliveryBoxInteraction {
    public static readonly byte[] OpenRequest = { 0 };
    public static bool IsOpenRequest(byte[] data) => data != null && data.Length >= 1 && data[0] == 0;
}

/// <summary>
/// Payload envoyé par le client pour appliquer une peinture depuis un PaintBucket.
/// wallIdx ou groundIdx + coverSettings.
/// Restera dans la couche métier existante (ApartmentController) — on achemine juste
/// l'événement d'ouverture du bucket via l'interaction.
/// 1 byte : 0 = open bucket UI.
/// </summary>
public static class PaintBucketInteraction {
    public static readonly byte[] OpenRequest = { 0 };
    public static bool IsOpenRequest(byte[] data) => data != null && data.Length >= 1 && data[0] == 0;
}
