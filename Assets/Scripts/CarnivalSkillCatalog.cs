using System;
using System.Collections.Generic;

// Central skill IDs, card rules, and shared combat tuning for solo and LAN play.
public enum CarnivalMainSkill : byte
{
    Cyclone, Flicker, Smite,
    TornadoShot, GalvanicArrow, CobraRash,
    Spark, IceNova, FreezingPulse
}

public enum CarnivalSupportSkill : byte
{
    // Shockwave is a sword-generated slash wave, not PoE's weapon-restricted gem.
    Shockwave, Bleed, Volatility, IncreasedArea, Multistrike,
    Chain, MultipleProjectiles, MirageArcher, Pierce, Barrage, Nimis,
    // SpellSequence follows the aim direction; SpellEcho repeats at the original point.
    SpellSequence, SpellEcho, SpellTotem,
    AddedAttackDamage, AttackSpeed, LifeLeech, MaxHealth
}

public static class CarnivalSkillCatalog
{
    public const int ChoiceCount = 3;
    public const float FlickerRange = 5f;
    public const float TornadoShotRange = FlickerRange;
    public const float DefaultDamageMultiplier = 1.15f;
    public const float DefaultSpeedMultiplier = 1.15f;
    public const float DefaultLifeLeech = .05f;
    public const float DefaultMaxHealth = 20f;
    public const float GalvanicDamageShare = .5f;
    public const float GalvanicConeReach = 2.6f;
    public const float GalvanicConeDegrees = 70f;
    public const int GalvanicBaseProjectiles = 3;
    public const int ChainSupportBonus = 1;
    public const float VolatilityMinimumMultiplier = .6f;
    public const float VolatilityMaximumMultiplier = 1.4f;
    public const float BleedDuration = 3f;
    public const float BleedDamagePerSecond = 1.2f;
    public static readonly CarnivalSupportSkill[] Defaults = {
        CarnivalSupportSkill.AddedAttackDamage, CarnivalSupportSkill.AttackSpeed,
        CarnivalSupportSkill.LifeLeech, CarnivalSupportSkill.MaxHealth
    };

    public static CarnivalMainSkill MainForWeapon(int weapon, int choice)
    {
        if (weapon < 0 || weapon > 2 || choice < 0 || choice >= ChoiceCount)
            throw new ArgumentOutOfRangeException();
        return (CarnivalMainSkill)(weapon * ChoiceCount + choice);
    }

    public static bool MatchesWeapon(int weapon, CarnivalMainSkill skill) =>
        weapon >= 0 && weapon <= 2 && (int)skill / ChoiceCount == weapon;

    public static string MainName(CarnivalMainSkill skill)
    {
        switch (skill)
        {
            case CarnivalMainSkill.TornadoShot: return "TORNADO SHOT";
            case CarnivalMainSkill.GalvanicArrow: return "GALVANIC ARROW";
            case CarnivalMainSkill.CobraRash: return "COBRA RASH";
            case CarnivalMainSkill.IceNova: return "ICE NOVA";
            case CarnivalMainSkill.FreezingPulse: return "FREEZING PULSE";
            default: return skill.ToString().ToUpperInvariant();
        }
    }

    public static string SupportName(CarnivalSupportSkill skill)
    {
        switch (skill)
        {
            case CarnivalSupportSkill.Shockwave: return "SHOCKWAVE SUPPORT";
            case CarnivalSupportSkill.Bleed: return "BLEED SUPPORT";
            case CarnivalSupportSkill.Volatility: return "VOLATILITY SUPPORT";
            case CarnivalSupportSkill.IncreasedArea: return "INCREASED AREA SUPPORT";
            case CarnivalSupportSkill.Multistrike: return "MULTISTRIKE SUPPORT";
            case CarnivalSupportSkill.Chain: return "CHAIN SUPPORT";
            case CarnivalSupportSkill.MultipleProjectiles: return "MULTIPLE PROJECTILES SUPPORT";
            case CarnivalSupportSkill.MirageArcher: return "MIRAGE ARCHER SUPPORT";
            case CarnivalSupportSkill.Pierce: return "PIERCE SUPPORT";
            case CarnivalSupportSkill.Barrage: return "BARRAGE SUPPORT";
            case CarnivalSupportSkill.Nimis: return "NIMIS SUPPORT";
            case CarnivalSupportSkill.SpellSequence: return "SPELL SEQUENCE SUPPORT";
            case CarnivalSupportSkill.SpellEcho: return "SPELL ECHO SUPPORT";
            case CarnivalSupportSkill.SpellTotem: return "SPELL TOTEM SUPPORT";
            case CarnivalSupportSkill.AddedAttackDamage: return "ALL DAMAGE UP";
            case CarnivalSupportSkill.AttackSpeed: return "ATTACK / CAST SPEED UP";
            case CarnivalSupportSkill.LifeLeech: return "LIFE LEECH";
            default: return "MAX HEALTH";
        }
    }

    public static bool IsDefault(CarnivalSupportSkill skill) =>
        skill >= CarnivalSupportSkill.AddedAttackDamage;

    public static bool IsCompatible(int weapon, CarnivalMainSkill main, CarnivalSupportSkill support)
    {
        if (!MatchesWeapon(weapon, main)) return false;
        if (IsDefault(support)) return true;
        if (weapon == 0)
            return support == CarnivalSupportSkill.Shockwave || support == CarnivalSupportSkill.Bleed ||
                support == CarnivalSupportSkill.Volatility ||
                support == CarnivalSupportSkill.IncreasedArea && main == CarnivalMainSkill.Cyclone ||
                support == CarnivalSupportSkill.Multistrike &&
                    (main == CarnivalMainSkill.Flicker || main == CarnivalMainSkill.Smite);
        if (weapon == 1)
            return support == CarnivalSupportSkill.MultipleProjectiles ||
                support == CarnivalSupportSkill.MirageArcher || support == CarnivalSupportSkill.Barrage ||
                support == CarnivalSupportSkill.Nimis ||
                support == CarnivalSupportSkill.Chain && main == CarnivalMainSkill.CobraRash ||
                support == CarnivalSupportSkill.Pierce &&
                    (main == CarnivalMainSkill.TornadoShot || main == CarnivalMainSkill.CobraRash);
        return support == CarnivalSupportSkill.SpellSequence ||
            support == CarnivalSupportSkill.SpellEcho || support == CarnivalSupportSkill.SpellTotem ||
            support == CarnivalSupportSkill.MultipleProjectiles &&
                (main == CarnivalMainSkill.Spark || main == CarnivalMainSkill.FreezingPulse);
    }

    // Cobra Rash is this game's bow skill; its first projectile has two innate jumps.
    public static int BaseChainCount(CarnivalMainSkill skill) =>
        skill == CarnivalMainSkill.CobraRash ? 2 : 0;

    public static CarnivalSupportSkill[] Offer(int weapon, CarnivalMainSkill main,
        ICollection<CarnivalSupportSkill> selected, Random random)
    {
        var primary = new List<CarnivalSupportSkill>();
        var defaults = new List<CarnivalSupportSkill>();
        foreach (CarnivalSupportSkill support in Enum.GetValues(typeof(CarnivalSupportSkill)))
        {
            if (!IsCompatible(weapon, main, support) || selected.Contains(support)) continue;
            (IsDefault(support) ? defaults : primary).Add(support);
        }
        var result = new CarnivalSupportSkill[ChoiceCount];
        for (int i = 0; i < result.Length; i++)
        {
            var pool = primary.Count > 0 ? primary : defaults;
            if (pool.Count == 0) throw new InvalidOperationException("No support choices remain.");
            int pick = random.Next(pool.Count);
            result[i] = pool[pick];
            pool.RemoveAt(pick);
        }
        return result;
    }

    public static float MinDamage(CarnivalMainSkill skill) =>
        skill <= CarnivalMainSkill.Smite ? 8f :
        skill <= CarnivalMainSkill.CobraRash ? 5f :
        skill == CarnivalMainSkill.Spark ? 1f :
        skill == CarnivalMainSkill.IceNova ? 20f : 10f;

    public static float MaxDamage(CarnivalMainSkill skill) =>
        skill <= CarnivalMainSkill.Smite ? 18f :
        skill <= CarnivalMainSkill.CobraRash ? 13f :
        skill == CarnivalMainSkill.Spark ? 10f :
        skill == CarnivalMainSkill.IceNova ? 30f : 15f;

    public static float RollDamage(CarnivalMainSkill skill, Random random, bool volatility)
    {
        float min = MinDamage(skill) * (volatility ? VolatilityMinimumMultiplier : 1f);
        float max = MaxDamage(skill) * (volatility ? VolatilityMaximumMultiplier : 1f);
        return (min + (float)random.NextDouble() * (max - min)) * AttackDamageMultiplier(skill);
    }

    // PoE 1 level 1 attack gem base damage. Spell base damage is not multiplied
    // by its added damage effectiveness.
    public static float AttackDamageMultiplier(CarnivalMainSkill skill)
    {
        switch (skill)
        {
            case CarnivalMainSkill.Cyclone: return .61f;
            case CarnivalMainSkill.Flicker: return 1.77f;
            case CarnivalMainSkill.Smite: return 2.25f;
            case CarnivalMainSkill.TornadoShot: return 1f;
            case CarnivalMainSkill.GalvanicArrow: return .92f;
            case CarnivalMainSkill.CobraRash: return 1.25f;
            default: return 1f;
        }
    }
}
