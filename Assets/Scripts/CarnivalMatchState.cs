using System;

// Match rules and clocks live here so a future dedicated server can own them.
// This class deliberately has no UnityEngine, scene, input, or UI dependency.
public enum CarnivalMonsterKind { Skitter, Spitter, Colossus, Ogre, Boss }
public enum CarnivalMonsterRarity : byte { Normal, Magic, Rare }
[Flags]
public enum CarnivalMonsterModifier : byte { None = 0, Hasted = 1, Brutal = 2, Fortified = 4 }

public static class CarnivalMatchRules
{
    public const int FinalStage = 5;
    public const double CombatSeconds = 30;
    public const double DraftSeconds = 5;
    public const double RespawnSeconds = 5;
    public const double WaveDelaySeconds = 1.2;
    public const int ShockwaveCost = 20;

    public static CarnivalMonsterRarity RollRarity(int stage, Random random)
    {
        if (stage >= FinalStage) return CarnivalMonsterRarity.Normal;
        double roll = random.NextDouble();
        double rareChance = stage == 1 ? .02 : stage == 2 ? .04 : stage == 3 ? .06 : .08;
        double magicChance = stage == 1 ? .12 : stage == 2 ? .16 : stage == 3 ? .20 : .24;
        return roll < rareChance ? CarnivalMonsterRarity.Rare :
            roll < rareChance + magicChance ? CarnivalMonsterRarity.Magic : CarnivalMonsterRarity.Normal;
    }

    public static CarnivalMonsterModifier RollModifiers(CarnivalMonsterRarity rarity, Random random)
    {
        int count = rarity == CarnivalMonsterRarity.Rare ? 2 : rarity == CarnivalMonsterRarity.Magic ? 1 : 0;
        if (count == 0) return CarnivalMonsterModifier.None;
        int first = random.Next(3);
        int second = (first + 1 + random.Next(2)) % 3;
        return (CarnivalMonsterModifier)((count > 0 ? 1 << first : 0) |
            (count > 1 ? 1 << second : 0));
    }

    public static float RarityHealthMultiplier(CarnivalMonsterRarity rarity) =>
        rarity == CarnivalMonsterRarity.Rare ? 2f : rarity == CarnivalMonsterRarity.Magic ? 1.5f : 1f;

    public static float ModifiedHealth(float baseHealth, CarnivalMonsterRarity rarity, CarnivalMonsterModifier mods) =>
        baseHealth * RarityHealthMultiplier(rarity) *
        ((mods & CarnivalMonsterModifier.Fortified) != 0 ? 1.3f : 1f);

    public static float ModifiedSpeed(float baseSpeed, CarnivalMonsterModifier mods) =>
        baseSpeed * ((mods & CarnivalMonsterModifier.Hasted) != 0 ? 1.25f : 1f);

    public static float ModifiedDamage(float baseDamage, CarnivalMonsterModifier mods) =>
        baseDamage * ((mods & CarnivalMonsterModifier.Brutal) != 0 ? 1.25f : 1f);

    public static int InitialMonsterCount(int stage) => stage == FinalStage ? 1 : (3 + stage * 2) * 3;

    public static CarnivalMonsterKind MonsterForStage(int stage, int index)
    {
        if (stage == FinalStage) return CarnivalMonsterKind.Boss;
        if (stage == 1) return CarnivalMonsterKind.Skitter;
        if (stage == 2) return index % 2 == 0 ? CarnivalMonsterKind.Skitter : CarnivalMonsterKind.Spitter;
        return (CarnivalMonsterKind)(index % (stage == 3 ? 3 : 4));
    }

    public static float MonsterHealth(CarnivalMonsterKind kind, int stage)
    {
        float baseHealth = kind == CarnivalMonsterKind.Boss ? 50 : kind == CarnivalMonsterKind.Ogre ? 12 :
            kind == CarnivalMonsterKind.Colossus ? 8 : kind == CarnivalMonsterKind.Spitter ? 4 : 2;
        float scale = stage == 1 ? .7f : stage == 2 ? .85f : stage == 3 ? 1f : stage == 4 ? 1.25f : 1f;
        return kind == CarnivalMonsterKind.Boss ? baseHealth : baseHealth * scale;
    }

    public static float MonsterSpeed(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? 1.15f : kind == CarnivalMonsterKind.Ogre ? .9f :
        kind == CarnivalMonsterKind.Colossus ? 1.2f : kind == CarnivalMonsterKind.Spitter ? 1.9f : 3.5f;

    public static float MonsterDamage(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? 34 : kind == CarnivalMonsterKind.Ogre ? 27 :
        kind == CarnivalMonsterKind.Colossus ? 16 : kind == CarnivalMonsterKind.Spitter ? 8 : 6;

    public static float MonsterRadius(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? 1.05f : kind == CarnivalMonsterKind.Ogre ? .85f :
        kind == CarnivalMonsterKind.Colossus ? .7f : kind == CarnivalMonsterKind.Spitter ? .48f : .35f;

    public static float AttackRange(CarnivalMonsterKind kind, float spacingRadius) =>
        kind == CarnivalMonsterKind.Spitter ? 9f :
        kind == CarnivalMonsterKind.Ogre ? (spacingRadius + .9f) * 1.5f : spacingRadius + .9f;

    public static float WindupDuration(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? 1.15f : kind == CarnivalMonsterKind.Ogre ? 1.4f :
        kind == CarnivalMonsterKind.Colossus ? .85f : kind == CarnivalMonsterKind.Spitter ? .8f :
        kind == CarnivalMonsterKind.Skitter ? .2f : 0f;

    public static float RecoveryDuration(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? .9f : kind == CarnivalMonsterKind.Ogre ? 1.1f :
        kind == CarnivalMonsterKind.Colossus ? .8f : kind == CarnivalMonsterKind.Spitter ? .8f : .55f;

    public static float ProjectileDamage(float baseDamage, int projectileCount, bool piercing) =>
        baseDamage * (projectileCount > 1 ? .8f : 1f) * (piercing ? .8f : 1f);

    public static float DamageAfterGuard(float damage, float multiplier) => Math.Max(0, damage * multiplier);

    public static float HealthAfterLifesteal(float health, float maxHealth, float actualDamage, float lifesteal) =>
        Math.Min(maxHealth, health + actualDamage * lifesteal);

    public static int ScoreForKill(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? 400 : kind == CarnivalMonsterKind.Ogre ? 50 :
        kind == CarnivalMonsterKind.Colossus ? 35 : kind == CarnivalMonsterKind.Spitter ? 20 : 10;

    public static int CpForKill(CarnivalMonsterKind kind) =>
        kind == CarnivalMonsterKind.Boss ? 0 : kind == CarnivalMonsterKind.Ogre ? 12 :
        kind == CarnivalMonsterKind.Colossus ? 9 : kind == CarnivalMonsterKind.Spitter ? 7 : 5;

    public static int RarityRewardMultiplier(CarnivalMonsterRarity rarity) =>
        rarity == CarnivalMonsterRarity.Rare ? 3 : rarity == CarnivalMonsterRarity.Magic ? 2 : 1;
}

// Collision math accepts plain numbers so it can run in a server process without a scene.
public static class CarnivalCombatGeometry
{
    public static bool InBeam(float originX, float originY, float directionX, float directionY,
        float targetX, float targetY, float length, float width, float radius)
    {
        float x = targetX - originX, y = targetY - originY;
        float forward = x * directionX + y * directionY;
        float side = Math.Abs(directionX * y - directionY * x);
        return forward >= -radius && forward <= length + radius && side <= width * .5f + radius;
    }

    public static bool InSector(float originX, float originY, float directionX, float directionY,
        float targetX, float targetY, float reach, float degrees)
    {
        float x = targetX - originX, y = targetY - originY;
        float distanceSquared = x * x + y * y;
        if (distanceSquared > reach * reach) return false;
        if (distanceSquared < .001f) return true;
        float directionLength = (float)Math.Sqrt(directionX * directionX + directionY * directionY);
        if (directionLength < .0001f) return false;
        float dot = (directionX * x + directionY * y) / (directionLength * (float)Math.Sqrt(distanceSquared));
        return dot >= (float)Math.Cos(degrees * Math.PI / 360.0);
    }

    public static float DistanceToSegment(float pointX, float pointY, float startX, float startY,
        float endX, float endY, out float alongPath)
    {
        float dx = endX - startX, dy = endY - startY;
        float lengthSquared = dx * dx + dy * dy;
        alongPath = lengthSquared > .000001f ?
            Math.Max(0, Math.Min(1, ((pointX - startX) * dx + (pointY - startY) * dy) / lengthSquared)) : 0;
        float x = pointX - (startX + dx * alongPath);
        float y = pointY - (startY + dy * alongPath);
        return (float)Math.Sqrt(x * x + y * y);
    }
}

public sealed class CarnivalMatchState
{
    public int Stage { get; private set; } = 1;
    public int Score { get; private set; }
    public int Cp { get; private set; }
    public double PhaseDeadline { get; private set; }
    public double NextWaveAt { get; private set; }
    public double RespawnAt { get; private set; }

    public void ResetForNewMatch(double now)
    {
        Stage = 1;
        Score = Cp = 0;
        NextWaveAt = RespawnAt = 0;
        PhaseDeadline = now + CarnivalMatchRules.CombatSeconds;
    }

    public void BeginDraft() => PhaseDeadline += CarnivalMatchRules.DraftSeconds;

    public void AdvanceStage()
    {
        if (Stage >= CarnivalMatchRules.FinalStage) throw new InvalidOperationException("Final stage already reached.");
        Stage++;
        NextWaveAt = 0;
        PhaseDeadline += CarnivalMatchRules.CombatSeconds;
    }

    public bool PhaseExpired(double now) => now >= PhaseDeadline;
    public bool IsRespawning => RespawnAt > 0;
    public bool RespawnReady(double now) => IsRespawning && now >= RespawnAt;
    public void BeginRespawn(double now) => RespawnAt = now + CarnivalMatchRules.RespawnSeconds;
    public void FinishRespawn() => RespawnAt = 0;
    public void EndMatch() => RespawnAt = 0;

    public void ScheduleWave(double now)
    {
        if (NextWaveAt <= 0) NextWaveAt = now + CarnivalMatchRules.WaveDelaySeconds;
    }

    public bool WaveReady(double now) => NextWaveAt > 0 && now >= NextWaveAt;
    public void ClearWaveSchedule() => NextWaveAt = 0;

    public void RegisterKill(CarnivalMonsterKind kind, CarnivalMonsterRarity rarity = CarnivalMonsterRarity.Normal)
    {
        int multiplier = CarnivalMatchRules.RarityRewardMultiplier(rarity);
        Score += CarnivalMatchRules.ScoreForKill(kind) * multiplier;
        Cp += CarnivalMatchRules.CpForKill(kind) * multiplier;
    }

    public bool TrySpendCp(int cost)
    {
        if (cost < 0 || Cp < cost) return false;
        Cp -= cost;
        return true;
    }
}
