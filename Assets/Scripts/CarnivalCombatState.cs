using System;
using System.Collections.Generic;

// Mutable combat data. This file has no Unity scene, input, or rendering dependency.
public enum CarnivalMonsterAction { Moving, Windup, Locked, Strike, Recovery }

public sealed class CarnivalMonsterState
{
    public int Id;
    public CarnivalMonsterKind Kind;
    public CarnivalMonsterRarity Rarity;
    public CarnivalMonsterModifier Modifiers;
    public CarnivalMonsterAction Action;
    public float X, Y, AttackX, AttackY;
    public float Health, MaxHealth, Radius, SpacingRadius, Speed, Damage;
    public float Cooldown, ActionTimer, Phase;
    public float BleedSeconds, BleedDamagePerSecond, ChillSeconds;
}

public sealed class CarnivalProjectileState
{
    public int Id;
    public float X, Y, VelocityX, VelocityY, Life, Radius, Damage;
    public bool Hostile, Piercing;
    public int ChainsRemaining;
    public bool SplitOnImpact, Returning;
    public float RangeRemaining;
    public int BouncesRemaining;
    public readonly HashSet<int> HitMonsterIds = new HashSet<int>();
}

public struct CarnivalMonsterAttackResult
{
    public float DirectDamage;
    public bool SpawnProjectile;
    public float ProjectileX, ProjectileY, VelocityX, VelocityY;
    public float ProjectileRadius, ProjectileLife, ProjectileDamage;
}

public sealed class CarnivalCombatState
{
    const float ArenaRadius = 15f;
    readonly List<CarnivalMonsterState> monsters = new List<CarnivalMonsterState>();
    readonly List<CarnivalProjectileState> projectiles = new List<CarnivalProjectileState>();
    int nextId = 1;
    float[] pillarX = new float[0], pillarY = new float[0];
    float pillarRadius;

    public float PlayerX, PlayerY, PlayerHealth = 100;
    public IReadOnlyList<CarnivalMonsterState> Monsters => monsters;
    public IReadOnlyList<CarnivalProjectileState> Projectiles => projectiles;

    public void SetPillars(float[] x, float[] y, float radius)
    {
        if (x == null || y == null || x.Length != y.Length) throw new ArgumentException("Pillar coordinates must match.");
        pillarX = (float[])x.Clone();
        pillarY = (float[])y.Clone();
        pillarRadius = radius;
    }

    public void Clear()
    {
        ClearEntities();
        nextId = 1;
        PlayerX = PlayerY = 0;
        PlayerHealth = 100;
    }

    public void ClearEntities()
    {
        monsters.Clear();
        projectiles.Clear();
    }

    public void Add(CarnivalMonsterState monster)
    {
        monster.Id = nextId++;
        monsters.Add(monster);
    }

    public void Add(CarnivalProjectileState projectile)
    {
        projectile.Id = nextId++;
        projectiles.Add(projectile);
    }

    public void Remove(CarnivalMonsterState monster) => monsters.Remove(monster);
    public void Remove(CarnivalProjectileState projectile) => projectiles.Remove(projectile);

    public void Clamp(ref float x, ref float y, float radius)
    {
        float edge = ArenaRadius - radius;
        x = Math.Max(-edge, Math.Min(edge, x));
        y = Math.Max(-edge, Math.Min(edge, y));
        for (int i = 0; i < pillarX.Length; i++)
        {
            float dx = x - pillarX[i], dy = y - pillarY[i];
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float clearance = pillarRadius + radius;
            if (distance >= clearance) continue;
            if (distance <= .001f) { x = pillarX[i] + clearance; y = pillarY[i]; }
            else { x = pillarX[i] + dx / distance * clearance; y = pillarY[i] + dy / distance * clearance; }
        }
    }

    public void MovePlayer(float directionX, float directionY, float speed, float dt, float radius)
    {
        float magnitude = (float)Math.Sqrt(directionX * directionX + directionY * directionY);
        if (magnitude > 1f) { directionX /= magnitude; directionY /= magnitude; }
        PlayerX += directionX * speed * dt;
        PlayerY += directionY * speed * dt;
        Clamp(ref PlayerX, ref PlayerY, radius);
    }

    public void DashPlayer(float directionX, float directionY, float distance, float radius)
    {
        float magnitude = (float)Math.Sqrt(directionX * directionX + directionY * directionY);
        if (magnitude <= .001f) return;
        PlayerX += directionX / magnitude * distance;
        PlayerY += directionY / magnitude * distance;
        Clamp(ref PlayerX, ref PlayerY, radius);
    }

    public bool ProjectileTouchesPillar(CarnivalProjectileState shot)
    {
        for (int i = 0; i < pillarX.Length; i++)
        {
            float dx = shot.X - pillarX[i], dy = shot.Y - pillarY[i];
            float radius = pillarRadius + shot.Radius;
            if (dx * dx + dy * dy < radius * radius) return true;
        }
        return false;
    }

    public bool AdvanceProjectile(CarnivalProjectileState shot, float dt)
    {
        float distance = (float)Math.Sqrt(shot.VelocityX * shot.VelocityX + shot.VelocityY * shot.VelocityY) * dt;
        if (shot.SplitOnImpact && shot.RangeRemaining > 0 && distance > shot.RangeRemaining)
        {
            dt *= shot.RangeRemaining / distance;
            distance = shot.RangeRemaining;
        }
        shot.X += shot.VelocityX * dt;
        shot.Y += shot.VelocityY * dt;
        if (shot.SplitOnImpact) shot.RangeRemaining = Math.Max(0, shot.RangeRemaining - distance);
        shot.Life -= dt;
        return shot.Life <= 0 || Math.Abs(shot.X) > ArenaRadius || Math.Abs(shot.Y) > ArenaRadius ||
            ProjectileTouchesPillar(shot);
    }

    public float DamageMonster(CarnivalMonsterState monster, float damage, float maxPlayerHealth, float lifesteal)
    {
        if (monster.Health <= 0 || damage <= 0) return 0;
        float actual = Math.Min(monster.Health, damage);
        monster.Health -= actual;
        if (lifesteal > 0)
            PlayerHealth = CarnivalMatchRules.HealthAfterLifesteal(PlayerHealth, maxPlayerHealth, actual, lifesteal);
        return actual;
    }

    public void ApplyBleed(CarnivalMonsterState monster)
    {
        if (monster.Health <= 0) return;
        monster.BleedSeconds = CarnivalSkillCatalog.BleedDuration;
        monster.BleedDamagePerSecond = CarnivalSkillCatalog.BleedDamagePerSecond;
    }

    public void ApplyChill(CarnivalMonsterState monster)
    {
        if (monster.Health > 0) monster.ChillSeconds = Math.Max(monster.ChillSeconds, 1.5f);
    }

    public void TickBleeds(float dt)
    {
        foreach (var monster in monsters)
        {
            if (monster.Health <= 0 || monster.BleedSeconds <= 0) continue;
            float active = Math.Min(dt, monster.BleedSeconds);
            monster.Health = Math.Max(0, monster.Health - monster.BleedDamagePerSecond * active);
            monster.BleedSeconds -= dt;
        }
    }

    public CarnivalMonsterState FirstProjectileHit(CarnivalProjectileState shot, float previousX, float previousY)
    {
        CarnivalMonsterState first = null;
        float firstAlongPath = float.PositiveInfinity;
        foreach (var monster in monsters)
        {
            if (monster.Health <= 0 || shot.HitMonsterIds.Contains(monster.Id)) continue;
            float alongPath;
            float distance = CarnivalCombatGeometry.DistanceToSegment(monster.X, monster.Y,
                previousX, previousY, shot.X, shot.Y, out alongPath);
            if (distance > shot.Radius + monster.Radius || alongPath >= firstAlongPath) continue;
            first = monster;
            firstAlongPath = alongPath;
        }
        return first;
    }

    public CarnivalMonsterState ChainTarget(CarnivalProjectileState shot, CarnivalMonsterState hit)
    {
        CarnivalMonsterState nearestTarget = null;
        float nearest = 9f;
        foreach (var candidate in monsters)
        {
            if (candidate.Health <= 0 || shot.HitMonsterIds.Contains(candidate.Id)) continue;
            float dx = candidate.X - hit.X, dy = candidate.Y - hit.Y;
            float distance = dx * dx + dy * dy;
            if (distance >= nearest) continue;
            nearest = distance;
            nearestTarget = candidate;
        }
        return nearestTarget;
    }

    public void ResolveContacts(float playerRadius, bool playerRespawning)
    {
        for (int pass = 0; pass < 5; pass++)
        {
            for (int i = 0; i < monsters.Count; i++)
                for (int j = i + 1; j < monsters.Count; j++)
                {
                    var a = monsters[i]; var b = monsters[j];
                    if (a.Health <= 0 || b.Health <= 0) continue;
                    float dx = b.X - a.X, dy = b.Y - a.Y;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    float overlap = a.Radius + b.Radius + .02f - distance;
                    if (overlap <= 0) continue;
                    bool aLocked = a.Action != CarnivalMonsterAction.Moving;
                    bool bLocked = b.Action != CarnivalMonsterAction.Moving;
                    if (aLocked && bLocked) continue;
                    float total = a.Radius + b.Radius;
                    float aShare = aLocked ? 0 : bLocked ? 1 : b.Radius / total;
                    float bShare = bLocked ? 0 : aLocked ? 1 : a.Radius / total;
                    float axisX = distance > .001f ? dx / distance : (float)Math.Cos(i * 2.4f + j);
                    float axisY = distance > .001f ? dy / distance : (float)Math.Sin(i * 2.4f + j);
                    a.X -= axisX * overlap * aShare; a.Y -= axisY * overlap * aShare;
                    b.X += axisX * overlap * bShare; b.Y += axisY * overlap * bShare;
                    Clamp(ref a.X, ref a.Y, a.Radius);
                    Clamp(ref b.X, ref b.Y, b.Radius);
                }
            if (playerRespawning) continue;
            foreach (var monster in monsters)
            {
                if (monster.Health <= 0) continue;
                float dx = monster.X - PlayerX, dy = monster.Y - PlayerY;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                float overlap = playerRadius + monster.Radius + .02f - distance;
                if (overlap <= 0) continue;
                float axisX = distance > .001f ? dx / distance : 0;
                float axisY = distance > .001f ? dy / distance : 1;
                if (monster.Action == CarnivalMonsterAction.Moving)
                {
                    monster.X += axisX * overlap; monster.Y += axisY * overlap;
                    Clamp(ref monster.X, ref monster.Y, monster.Radius);
                }
                else
                {
                    PlayerX -= axisX * overlap; PlayerY -= axisY * overlap;
                    Clamp(ref PlayerX, ref PlayerY, playerRadius);
                }
            }
        }
    }

    // Returns true exactly when the attack should resolve. Windup and recovery never move.
    public bool AdvanceMonster(CarnivalMonsterState m, float dt, float now)
    {
        if (m.Health <= 0) return false;
        float actionSpeed = (m.Modifiers & CarnivalMonsterModifier.Hasted) != 0 ? 1.25f : 1f;
        if (m.ChillSeconds > 0) m.ChillSeconds = Math.Max(0, m.ChillSeconds - dt);
        float movementSpeed = m.Speed * (m.ChillSeconds > 0 ? .6f : 1f);
        float dx = PlayerX - m.X, dy = PlayerY - m.Y;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy);
        float directionX = distance > .001f ? dx / distance : 1f;
        float directionY = distance > .001f ? dy / distance : 0f;
        if (m.Action != CarnivalMonsterAction.Moving)
        {
            if (m.Action == CarnivalMonsterAction.Windup && m.Kind == CarnivalMonsterKind.Spitter)
            { m.AttackX = directionX; m.AttackY = directionY; }
            m.ActionTimer -= dt * actionSpeed;
            if (m.ActionTimer > 0) return false;
            if (m.Action == CarnivalMonsterAction.Windup || m.Action == CarnivalMonsterAction.Locked)
            {
                m.ActionTimer = m.Action == CarnivalMonsterAction.Locked ? .20f : .18f;
                m.Action = CarnivalMonsterAction.Strike;
                return true;
            }
            if (m.Action == CarnivalMonsterAction.Strike)
            { m.Action = CarnivalMonsterAction.Recovery; m.ActionTimer = CarnivalMatchRules.RecoveryDuration(m.Kind); }
            else { m.Action = CarnivalMonsterAction.Moving; m.Cooldown = .15f; }
            return false;
        }

        m.Cooldown = Math.Max(0, m.Cooldown - dt * actionSpeed);
        float range = CarnivalMatchRules.AttackRange(m.Kind, m.SpacingRadius);
        if (m.Cooldown <= 0 && distance <= range)
        {
            m.AttackX = directionX; m.AttackY = directionY;
            m.Action = CarnivalMonsterAction.Windup;
            m.ActionTimer = CarnivalMatchRules.WindupDuration(m.Kind);
        }
        else if (m.Kind == CarnivalMonsterKind.Spitter)
        {
            if (distance > 5f) { m.X += directionX * movementSpeed * dt; m.Y += directionY * movementSpeed * dt; }
            else if (distance < 3.5f) { m.X -= directionX * movementSpeed * dt; m.Y -= directionY * movementSpeed * dt; }
            Clamp(ref m.X, ref m.Y, m.Radius);
        }
        else if (distance > range * .8f)
        {
            float sidestep = m.Kind == CarnivalMonsterKind.Skitter ?
                (float)Math.Sin(now * 8f + m.Phase) * .28f : 0;
            float moveX = directionX - directionY * sidestep;
            float moveY = directionY + directionX * sidestep;
            float magnitude = (float)Math.Sqrt(moveX * moveX + moveY * moveY);
            if (magnitude > .001f) { m.X += moveX / magnitude * movementSpeed * dt; m.Y += moveY / magnitude * movementSpeed * dt; }
            Clamp(ref m.X, ref m.Y, m.Radius);
        }
        return false;
    }

    public CarnivalMonsterAttackResult ResolveMonsterAttack(CarnivalMonsterState monster, float playerRadius)
    {
        var result = new CarnivalMonsterAttackResult();
        float range = CarnivalMatchRules.AttackRange(monster.Kind, monster.SpacingRadius);
        if (monster.Kind == CarnivalMonsterKind.Spitter)
        {
            float directionX = monster.AttackX, directionY = monster.AttackY;
            float length = (float)Math.Sqrt(directionX * directionX + directionY * directionY);
            if (length < .001f) { directionX = 0; directionY = 1; }
            else { directionX /= length; directionY /= length; }
            result.SpawnProjectile = true;
            result.ProjectileX = monster.X + directionX * (monster.Radius + .35f);
            result.ProjectileY = monster.Y + directionY * (monster.Radius + .35f);
            result.VelocityX = directionX * 7f;
            result.VelocityY = directionY * 7f;
            result.ProjectileRadius = .28f;
            result.ProjectileLife = 2f;
            result.ProjectileDamage = monster.Damage;
        }
        else if (monster.Kind == CarnivalMonsterKind.Ogre)
        {
            if (CarnivalCombatGeometry.InSector(monster.X, monster.Y, monster.AttackX, monster.AttackY,
                PlayerX, PlayerY, range + playerRadius, 90f))
                result.DirectDamage = monster.Damage;
        }
        else
        {
            float dx = PlayerX - monster.X, dy = PlayerY - monster.Y;
            if (dx * dx + dy * dy <= range * range) result.DirectDamage = monster.Damage;
        }
        return result;
    }
}
