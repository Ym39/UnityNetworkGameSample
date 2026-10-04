using System.IO;
using UnityEngine;

/// <summary>
/// Wire format for the packets exchanged between players. Everything here stays
/// far below the EOS P2P limit of <see cref="Epic.OnlineServices.P2P.P2PInterface.MAX_PACKET_SIZE"/>
/// (1170 bytes), so no fragmentation is needed.
/// </summary>
public enum NetworkMessageType : byte
{
    None = 0,

    /// <summary>The sender clicked a new destination. Payload: target x/z.</summary>
    Move = 1,

    /// <summary>Periodic correction from the owner. Payload: position x/z and yaw.</summary>
    Sync = 2,
}

public static class NetworkMessage
{
    /// <summary>
    /// Only the horizontal plane is sent: characters walk on the ground, so the
    /// receiver keeps its own height and nothing is gained by shipping Y.
    /// </summary>
    public static byte[] WriteMove(Vector3 target)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)NetworkMessageType.Move);
        writer.Write(target.x);
        writer.Write(target.z);

        return stream.ToArray();
    }

    public static byte[] WriteSync(Vector3 position, float yaw)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)NetworkMessageType.Sync);
        writer.Write(position.x);
        writer.Write(position.z);
        writer.Write(yaw);

        return stream.ToArray();
    }

    public static NetworkMessageType ReadType(byte[] data)
    {
        return data == null || data.Length == 0
            ? NetworkMessageType.None
            : (NetworkMessageType)data[0];
    }

    public static bool TryReadMove(byte[] data, out Vector2 target)
    {
        target = Vector2.zero;

        if (data == null || data.Length < 1 + sizeof(float) * 2)
        {
            return false;
        }

        using var reader = new BinaryReader(new MemoryStream(data));
        reader.ReadByte();
        target = new Vector2(reader.ReadSingle(), reader.ReadSingle());

        return true;
    }

    public static bool TryReadSync(byte[] data, out Vector2 position, out float yaw)
    {
        position = Vector2.zero;
        yaw = 0.0f;

        if (data == null || data.Length < 1 + sizeof(float) * 3)
        {
            return false;
        }

        using var reader = new BinaryReader(new MemoryStream(data));
        reader.ReadByte();
        position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        yaw = reader.ReadSingle();

        return true;
    }
}
