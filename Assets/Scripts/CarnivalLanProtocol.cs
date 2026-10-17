using System;
using System.IO;

public enum CarnivalLanPhase : byte { Waiting, Draft, Combat, Finished }
public enum CarnivalLanAction : byte { None, Dash, Summon, ChooseCard, Shockwave }
public enum CarnivalLanWeapon : byte { Sword, Bow, Wand }

public sealed class CarnivalLanInput
{
    public float MoveX, MoveY, AimX, AimY;
    public bool Attack;
    public int ActionId;
    public CarnivalLanAction Action;
    public byte ActionValue;
}

public sealed class CarnivalLanMonsterView
{
    public int Id;
    public byte Kind, Rarity, Action;
    public float X, Y, HealthRatio;
}

public sealed class CarnivalLanShotView
{
    public int Id;
    public bool Hostile;
    public float X, Y, Radius;
}

public sealed class CarnivalLanSnapshot
{
    public byte Slot, Stage;
    public CarnivalLanPhase Phase;
    public float SecondsLeft, X, Y, Health, MaxHealth, EnemyHealth;
    public int Cp, Score, EnemyScore, AckActionId;
    public bool Respawning;
    public byte[] Cards = new byte[3];
    public CarnivalLanMonsterView[] Monsters = new CarnivalLanMonsterView[0];
    public CarnivalLanShotView[] Shots = new CarnivalLanShotView[0];
}

public static class CarnivalLanProtocol
{
    public const int Port = 7777;
    public const byte Hello = 1, Input = 2, Snapshot = 3;
    public const byte Version = 3;

    public static byte[] HelloPacket(CarnivalLanWeapon weapon, CarnivalMainSkill mainSkill)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        { writer.Write(Hello); writer.Write(Version); writer.Write((byte)weapon);
            writer.Write((byte)mainSkill); return stream.ToArray(); }
    }

    public static byte[] InputPacket(CarnivalLanInput input)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Input); writer.Write(Version);
            writer.Write(input.MoveX); writer.Write(input.MoveY);
            writer.Write(input.AimX); writer.Write(input.AimY);
            writer.Write(input.Attack); writer.Write(input.ActionId);
            writer.Write((byte)input.Action); writer.Write(input.ActionValue);
            return stream.ToArray();
        }
    }

    public static bool ReadInput(byte[] packet, out CarnivalLanInput input)
    {
        input = null;
        if (packet == null || packet.Length != 25 || packet[0] != Input || packet[1] != Version) return false;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(packet)))
            {
                reader.ReadByte(); reader.ReadByte();
                input = new CarnivalLanInput {
                    MoveX = SafeAxis(reader.ReadSingle()), MoveY = SafeAxis(reader.ReadSingle()),
                    AimX = SafeAxis(reader.ReadSingle()), AimY = SafeAxis(reader.ReadSingle()),
                    Attack = reader.ReadBoolean(), ActionId = reader.ReadInt32(),
                    Action = (CarnivalLanAction)reader.ReadByte(), ActionValue = reader.ReadByte()
                };
                return true;
            }
        }
        catch (Exception) { return false; }
    }

    static float SafeAxis(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 :
        Math.Max(-1, Math.Min(1, value));

    public static byte[] SnapshotPacket(CarnivalLanSnapshot snapshot)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Snapshot); writer.Write(Version);
            writer.Write(snapshot.Slot); writer.Write(snapshot.Stage); writer.Write((byte)snapshot.Phase);
            writer.Write(snapshot.SecondsLeft); writer.Write(snapshot.X); writer.Write(snapshot.Y);
            writer.Write(snapshot.Health); writer.Write(snapshot.MaxHealth); writer.Write(snapshot.EnemyHealth);
            writer.Write(snapshot.Cp); writer.Write(snapshot.Score); writer.Write(snapshot.EnemyScore);
            writer.Write(snapshot.AckActionId); writer.Write(snapshot.Respawning);
            for (int i = 0; i < 3; i++) writer.Write(snapshot.Cards[i]);
            writer.Write((ushort)snapshot.Monsters.Length);
            foreach (var m in snapshot.Monsters)
            { writer.Write(m.Id); writer.Write(m.Kind); writer.Write(m.Rarity); writer.Write(m.Action); writer.Write(m.X); writer.Write(m.Y); writer.Write(m.HealthRatio); }
            writer.Write((ushort)snapshot.Shots.Length);
            foreach (var shot in snapshot.Shots)
            { writer.Write(shot.Id); writer.Write(shot.Hostile); writer.Write(shot.X); writer.Write(shot.Y); writer.Write(shot.Radius); }
            return stream.ToArray();
        }
    }

    public static bool ReadSnapshot(byte[] packet, out CarnivalLanSnapshot snapshot)
    {
        snapshot = null;
        if (packet == null || packet.Length < 53 || packet[0] != Snapshot || packet[1] != Version) return false;
        try
        {
            using (var reader = new BinaryReader(new MemoryStream(packet)))
            {
                reader.ReadByte(); reader.ReadByte();
                var value = new CarnivalLanSnapshot {
                    Slot = reader.ReadByte(), Stage = reader.ReadByte(), Phase = (CarnivalLanPhase)reader.ReadByte(),
                    SecondsLeft = reader.ReadSingle(), X = reader.ReadSingle(), Y = reader.ReadSingle(),
                    Health = reader.ReadSingle(), MaxHealth = reader.ReadSingle(), EnemyHealth = reader.ReadSingle(),
                    Cp = reader.ReadInt32(), Score = reader.ReadInt32(), EnemyScore = reader.ReadInt32(),
                    AckActionId = reader.ReadInt32(), Respawning = reader.ReadBoolean()
                };
                for (int i = 0; i < 3; i++) value.Cards[i] = reader.ReadByte();
                int monsters = reader.ReadUInt16();
                if (monsters > 200) return false;
                value.Monsters = new CarnivalLanMonsterView[monsters];
                for (int i = 0; i < monsters; i++) value.Monsters[i] = new CarnivalLanMonsterView {
                    Id = reader.ReadInt32(), Kind = reader.ReadByte(), Rarity = reader.ReadByte(), Action = reader.ReadByte(),
                    X = reader.ReadSingle(), Y = reader.ReadSingle(), HealthRatio = reader.ReadSingle()
                };
                int shots = reader.ReadUInt16();
                if (shots > 200) return false;
                value.Shots = new CarnivalLanShotView[shots];
                for (int i = 0; i < shots; i++) value.Shots[i] = new CarnivalLanShotView {
                    Id = reader.ReadInt32(), Hostile = reader.ReadBoolean(), X = reader.ReadSingle(),
                    Y = reader.ReadSingle(), Radius = reader.ReadSingle()
                };
                snapshot = value;
                return true;
            }
        }
        catch (Exception) { return false; }
    }
}
