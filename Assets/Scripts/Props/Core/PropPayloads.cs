using System;
using Sim.Enums;
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

public struct PropStateHeader
{
    public bool IsBuilt;
    public int PresetId; // -1 = pas de preset appliqué

    public const int ByteSize = 5;

    public static PropStateHeader Default => new PropStateHeader { IsBuilt = true, PresetId = -1 };

    // ── Sérialisation ─────────────────────────────────────────────────────────

    public void WriteTo(byte[] buffer, int offset = 0)
    {
        buffer[offset] = IsBuilt ? (byte)1 : (byte)0;
        BitConverter.GetBytes(PresetId).CopyTo(buffer, offset + 1);
    }

    public static PropStateHeader ReadFrom(byte[] data, int offset = 0)
    {
        if (data == null || data.Length < offset + ByteSize)
            return Default;
        return new PropStateHeader
        {
            IsBuilt = data[offset] != 0,
            PresetId = BitConverter.ToInt32(data, offset + 1)
        };
    }
}

// ─── Generic (mobilier sans état interactif spécial) ──────────────────────────
// Payload : header seul (5 bytes)
// Exemples : table, étagère, lampe de sol

public struct GenericPropState
{
    public PropStateHeader Header;

    public byte[] Serialize()
    {
        byte[] buf = new byte[PropStateHeader.ByteSize];
        Header.WriteTo(buf, 0);
        return buf;
    }

    public static GenericPropState Deserialize(byte[] data) =>
        new GenericPropState { Header = PropStateHeader.ReadFrom(data) };
}

// ─── Door ─────────────────────────────────────────────────────────────────────
// Payload : header(5) + isOpen(1) + lockState(1) + doorNumber(4) = 11 bytes
// LockState (Sim.Building.DoorLockState) : 0 = LOCKED, 1 = UNLOCKED
// DoorNumber : 0 pour les portes intérieures ; numéro affiché pour les portes d'entrée

public struct DoorState
{
    public PropStateHeader Header;
    public bool IsOpen;
    public DoorLockState LockState;
    public int DoorNumber;

    public byte[] Serialize()
    {
        byte[] buf = new byte[PropStateHeader.ByteSize + 1 + 1 + 4];
        Header.WriteTo(buf, 0);
        int off = PropStateHeader.ByteSize;
        buf[off++] = IsOpen ? (byte)1 : (byte)0;
        buf[off++] = (byte)LockState;
        BitConverter.GetBytes(DoorNumber).CopyTo(buf, off);
        return buf;
    }

    public static DoorState Deserialize(byte[] data)
    {
        if (data == null || data.Length < PropStateHeader.ByteSize + 1)
        {
            return new DoorState { Header = PropStateHeader.Default };
        }

        int off = PropStateHeader.ByteSize;
        bool isOpen = data[off++] != 0;
        DoorLockState lockState = data.Length > off
            ? (DoorLockState)data[off++]
            : DoorLockState.UNLOCKED;
        int doorNumber = data.Length >= off + 4 ? BitConverter.ToInt32(data, off) : 0;
        return new DoorState
        {
            Header = PropStateHeader.ReadFrom(data, 0),
            IsOpen = isOpen,
            LockState = lockState,
            DoorNumber = doorNumber
        };
    }
}

// ─── Seat ─────────────────────────────────────────────────────────────────────
// Payload : header(5) + seatCount(1) + [seatNetId(4) × N] + couchCount(1) + [couchNetId(4) × M]
// netId == 0 → slot libre

public struct SeatState
{
    public PropStateHeader Header;
    public uint[] SeatOccupants; // one entry per seat slot, 0 = empty
    public uint[] CouchOccupants; // one entry per couch slot, 0 = empty

    public byte[] Serialize()
    {
        int n = SeatOccupants?.Length ?? 0;
        int m = CouchOccupants?.Length ?? 0;
        byte[] buf = new byte[PropStateHeader.ByteSize + 1 + 4 * n + 1 + 4 * m];
        int off = 0;
        Header.WriteTo(buf, off);
        off += PropStateHeader.ByteSize;
        buf[off++] = (byte)n;
        for (int i = 0; i < n; i++)
        {
            BitConverter.GetBytes(SeatOccupants[i]).CopyTo(buf, off);
            off += 4;
        }

        buf[off++] = (byte)m;
        for (int i = 0; i < m; i++)
        {
            BitConverter.GetBytes(CouchOccupants[i]).CopyTo(buf, off);
            off += 4;
        }

        return buf;
    }

    public static SeatState Deserialize(byte[] data)
    {
        int minLen = PropStateHeader.ByteSize + 2;
        if (data == null || data.Length < minLen)
            return new SeatState
            {
                Header = PropStateHeader.Default, SeatOccupants = System.Array.Empty<uint>(),
                CouchOccupants = System.Array.Empty<uint>()
            };

        int off = 0;
        PropStateHeader header = PropStateHeader.ReadFrom(data, off);
        off += PropStateHeader.ByteSize;
        int n = data[off++];
        uint[] seats = new uint[n];
        for (int i = 0; i < n; i++)
        {
            seats[i] = BitConverter.ToUInt32(data, off);
            off += 4;
        }

        int m = (off < data.Length) ? data[off++] : 0;
        uint[] couches = new uint[m];
        for (int i = 0; i < m; i++)
        {
            couches[i] = BitConverter.ToUInt32(data, off);
            off += 4;
        }

        return new SeatState { Header = header, SeatOccupants = seats, CouchOccupants = couches };
    }
}

// ─── PaintBucket ──────────────────────────────────────────────────────────────
// Payload : header(5) + paintConfigId(4) + r(4) + g(4) + b(4) = 21 bytes
// Migration directe de PaintBucket.color (SyncVar Color) + paintConfigId (SyncVar int)

public struct PaintBucketState
{
    public PropStateHeader Header;
    public int PaintConfigId;
    public float R, G, B;

    public Color Color => new Color(R, G, B);

    public byte[] Serialize()
    {
        byte[] buf = new byte[PropStateHeader.ByteSize + 16];
        Header.WriteTo(buf, 0);
        int off = PropStateHeader.ByteSize;
        BitConverter.GetBytes(PaintConfigId).CopyTo(buf, off);
        off += 4;
        BitConverter.GetBytes(R).CopyTo(buf, off);
        off += 4;
        BitConverter.GetBytes(G).CopyTo(buf, off);
        off += 4;
        BitConverter.GetBytes(B).CopyTo(buf, off);
        return buf;
    }

    public static PaintBucketState Deserialize(byte[] data)
    {
        int off = PropStateHeader.ByteSize;
        if (data == null || data.Length < off + 16)
            return new PaintBucketState { Header = PropStateHeader.Default };
        return new PaintBucketState
        {
            Header = PropStateHeader.ReadFrom(data, 0),
            PaintConfigId = BitConverter.ToInt32(data, off),
            R = BitConverter.ToSingle(data, off + 4),
            G = BitConverter.ToSingle(data, off + 8),
            B = BitConverter.ToSingle(data, off + 12)
        };
    }
}

// ─── DeliveryBox ──────────────────────────────────────────────────────────────
// Payload : header(5) + deliveryCount(4) = 9 bytes
// Migration de DeliveryBox.deliveryCount (SyncVar uint)
// Le contenu des livraisons (Delivery[]) est récupéré via REST lors de l'ouverture
// et transmis par S2C_DeliveryBoxOpened — il n'est pas stocké dans le state.

public struct DeliveryBoxState
{
    public PropStateHeader Header;
    public uint DeliveryCount;

    public byte[] Serialize()
    {
        byte[] buf = new byte[PropStateHeader.ByteSize + 4];
        Header.WriteTo(buf, 0);
        BitConverter.GetBytes(DeliveryCount).CopyTo(buf, PropStateHeader.ByteSize);
        return buf;
    }

    public static DeliveryBoxState Deserialize(byte[] data)
    {
        int off = PropStateHeader.ByteSize;
        return new DeliveryBoxState
        {
            Header = PropStateHeader.ReadFrom(data, 0),
            DeliveryCount = (data != null && data.Length >= off + 4)
                ? BitConverter.ToUInt32(data, off)
                : 0u
        };
    }
}

// ─── Dispenser ────────────────────────────────────────────────────────────────
// Payload : header seul (5 bytes)
// Le catalogue d'articles vient du ScriptableObject DispenserConfiguration,
// pas du state réseau. Seul le state visuel (built + preset) est synchronisé.

public struct DispenserState
{
    public PropStateHeader Header;

    public byte[] Serialize()
    {
        byte[] buf = new byte[PropStateHeader.ByteSize];
        Header.WriteTo(buf, 0);
        return buf;
    }

    public static DispenserState Deserialize(byte[] data) =>
        new DispenserState { Header = PropStateHeader.ReadFrom(data) };
}

// ─── Package ──────────────────────────────────────────────────────────────────
// Payload : header(5) + propsConfigId(4) = 9 bytes
// Le PropsConfig contenu dans le colis est configuré dans l'éditeur
// et transit via le state réseau. L'ouverture est client-local.

public struct PackageState
{
    public PropStateHeader Header;
    public int PropsConfigId; // ID du PropsConfig à l'intérieur du colis

    public byte[] Serialize()
    {
        byte[] buf = new byte[PropStateHeader.ByteSize + 4];
        Header.WriteTo(buf, 0);
        BitConverter.GetBytes(PropsConfigId).CopyTo(buf, PropStateHeader.ByteSize);
        return buf;
    }

    public static PackageState Deserialize(byte[] data)
    {
        int off = PropStateHeader.ByteSize;
        return new PackageState
        {
            Header = PropStateHeader.ReadFrom(data),
            PropsConfigId = (data != null && data.Length >= off + 4)
                ? BitConverter.ToInt32(data, off)
                : 0
        };
    }
}

// ─── Payloads d'interaction C2S ───────────────────────────────────────────────

// ─── Generic prop interactions ────────────────────────────────────────────────
// PropType.Generic avec les payloads suivants :
//   0 = BUILD (le joueur construit un meuble non encore posé)

public static class GenericPropInteraction
{
    public static readonly byte[] BuildRequest = { 0 };
    public static bool IsBuildRequest(byte[] d) => d != null && d.Length >= 1 && d[0] == 0;
}

public static class SeatInteraction
{
    public static readonly byte[] SitRequest = { 0 };
    public static readonly byte[] RevokeRequest = { 1 };
    public static readonly byte[] CouchRequest = { 2 };

    public static bool IsSitRequest(byte[] d) => d != null && d.Length >= 1 && d[0] == 0;
    public static bool IsRevokeRequest(byte[] d) => d != null && d.Length >= 1 && d[0] == 1;
    public static bool IsCouchRequest(byte[] d) => d != null && d.Length >= 1 && d[0] == 2;
}

/// <summary>
/// Payload envoyé par le client pour acheter un article dans un Dispenser.
/// 4 bytes : itemId (int32 LE).
/// </summary>
public static class DispenserInteraction
{
    public static byte[] BuyRequest(int itemId) => BitConverter.GetBytes(itemId);

    public static int GetItemId(byte[] data) =>
        (data != null && data.Length >= 4) ? BitConverter.ToInt32(data, 0) : -1;
}

/// <summary>
/// Payload envoyé par le client pour ouvrir sa boîte aux livraisons.
/// 1 byte : 0 = open.
/// </summary>
public static class DeliveryBoxInteraction
{
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
public static class PaintBucketInteraction
{
    public static readonly byte[] OpenRequest = { 0 };
    public static bool IsOpenRequest(byte[] data) => data != null && data.Length >= 1 && data[0] == 0;
}

/// <summary>
/// Payload envoyé par le client pour ouvrir un colis (Package).
/// 1 byte : 0 = open.
/// L'ouverture déclenche le mode construction avec le PropsConfig contenu.
/// </summary>
public static class PackageInteraction
{
    public static readonly byte[] OpenRequest = { 0 };
    public static bool IsOpenRequest(byte[] data) => data != null && data.Length >= 1 && data[0] == 0;
}