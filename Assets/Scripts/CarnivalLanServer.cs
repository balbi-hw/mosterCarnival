using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

// Run a Windows/Linux Dedicated Server build with -carnival-server [-carnival-port 7777].
public sealed class CarnivalLanServer : MonoBehaviour
{
    sealed class Lane
    {
        public IPEndPoint Endpoint;
        public double LastSeen;
        public CarnivalLanWeapon Weapon;
        public CarnivalMainSkill MainSkill;
        public readonly HashSet<CarnivalSupportSkill> Supports = new HashSet<CarnivalSupportSkill>();
        public readonly CarnivalCombatState Combat = new CarnivalCombatState();
        public readonly CarnivalMatchState Match = new CarnivalMatchState();
        public CarnivalLanInput Input = new CarnivalLanInput { AimY = 1 };
        public int LastActionId;
        public readonly byte[] Cards = new byte[3];
        public int ChosenCard = -1;
        public float MaxHealth = 100, Damage = 1, MoveSpeed = 1, AttackSpeed = 1, Area = 1;
        public float DashDistance = 1, Guard = 1, Lifesteal, AttackCooldown, Invulnerable;
        public int MaxDashes = 1, Dashes = 1, Projectiles = 1, Chains;
        public float DashRecharge;
        public float AutonomousTime, AutonomousCooldown, AutonomousX, AutonomousY;
        public float BarrageCooldown;
        public int BarragePending;
        public bool Piercing, Cyclone;
    }

    static readonly int[] SummonCosts = { 10, 16, 24, 30 };
    static readonly CarnivalMonsterKind[] SummonKinds = {
        CarnivalMonsterKind.Skitter, CarnivalMonsterKind.Spitter,
        CarnivalMonsterKind.Colossus, CarnivalMonsterKind.Ogre
    };
    readonly Lane[] lanes = new Lane[2];
    readonly System.Random random = new System.Random();
    UdpClient socket;
    CarnivalLanPhase phase = CarnivalLanPhase.Waiting;
    double phaseEnds, previousTick, nextSnapshot;
    float accumulator;
    int port = CarnivalLanProtocol.Port;

    public static bool IsServerMode
    {
        get
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg == "-carnival-server") return true;
            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (IsServerMode && FindAnyObjectByType<CarnivalLanServer>() == null)
            new GameObject("Carnival LAN Dedicated Server").AddComponent<CarnivalLanServer>();
    }

    void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-carnival-port" && int.TryParse(args[i + 1], out int requested) &&
                requested > 0 && requested <= 65535) port = requested;
        try { socket = new UdpClient(new IPEndPoint(IPAddress.Any, port)); }
        catch (SocketException error) { Debug.LogError("LAN server port " + port + ": " + error.Message); enabled = false; return; }
        previousTick = Time.realtimeSinceStartupAsDouble;
        nextSnapshot = previousTick;
        Debug.Log("Carnival LAN server listening on UDP " + port);
    }

    void OnDestroy() { if (socket != null) socket.Close(); }

    void Update()
    {
        if (socket == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        for (int received = 0; socket.Available > 0 && received < 128; received++)
        {
            try
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] packet = socket.Receive(ref from);
                Receive(packet, from, now);
            }
            catch (SocketException error)
            {
                if (error.SocketErrorCode != SocketError.ConnectionReset)
                    Debug.LogWarning("LAN receive: " + error.Message);
                break;
            }
        }

        for (int i = 0; i < 2; i++)
            if (lanes[i] != null && now - lanes[i].LastSeen > 12)
            { Debug.Log("LAN player " + i + " timed out"); lanes[i] = null; phase = CarnivalLanPhase.Waiting; }

        accumulator += (float)Math.Min(.2, now - previousTick);
        previousTick = now;
        for (int ticks = 0; accumulator >= .05f && ticks < 4; ticks++)
        { Tick(.05f, now); accumulator -= .05f; }

        if (now >= nextSnapshot)
        {
            for (int i = 0; i < 2; i++) if (lanes[i] != null) SendSnapshot(i, now);
            nextSnapshot = now + .1;
        }
    }

    int FindSlot(IPEndPoint endpoint)
    {
        for (int i = 0; i < 2; i++)
            if (lanes[i] != null && lanes[i].Endpoint.Equals(endpoint)) return i;
        return -1;
    }

    void Receive(byte[] packet, IPEndPoint from, double now)
    {
        if (packet.Length < 2 || packet[1] != CarnivalLanProtocol.Version) return;
        int slot = FindSlot(from);
        if (packet[0] == CarnivalLanProtocol.Hello && packet.Length == 4)
        {
            if (packet[2] > 2 || !CarnivalSkillCatalog.MatchesWeapon(packet[2],
                (CarnivalMainSkill)packet[3])) return;
            if (slot < 0)
            {
                slot = lanes[0] == null ? 0 : lanes[1] == null ? 1 : -1;
                if (slot < 0) return;
                lanes[slot] = new Lane { Endpoint = from, LastSeen = now,
                    Weapon = (CarnivalLanWeapon)packet[2], MainSkill = (CarnivalMainSkill)packet[3] };
                ConfigureArena(lanes[slot].Combat);
                Debug.Log("LAN player " + slot + " joined from " + from);
            }
            lanes[slot].LastSeen = now;
            if (lanes[0] != null && lanes[1] != null && phase == CarnivalLanPhase.Waiting) StartMatch(now);
            SendSnapshot(slot, now);
            return;
        }
        if (slot < 0 || !CarnivalLanProtocol.ReadInput(packet, out CarnivalLanInput input)) return;
        Lane lane = lanes[slot];
        lane.LastSeen = now;
        lane.Input = input;
        if (input.ActionId > lane.LastActionId && input.ActionId <= lane.LastActionId + 1)
        {
            lane.LastActionId = input.ActionId;
            ProcessAction(slot, input);
        }
    }

    void ConfigureArena(CarnivalCombatState combat)
    {
        var x = new float[CarnivalArena3D.Pillars.Length];
        var y = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        { x[i] = CarnivalArena3D.Pillars[i].x; y[i] = CarnivalArena3D.Pillars[i].y; }
        combat.SetPillars(x, y, CarnivalArena3D.PillarRadius);
    }

    void StartMatch(double now)
    {
        for (int i = 0; i < 2; i++)
        {
            Lane lane = lanes[i];
            lane.Combat.Clear();
            lane.Match.ResetForNewMatch(now);
            lane.Supports.Clear();
            lane.ChosenCard = -1;
            lane.MaxHealth = lane.Combat.PlayerHealth = 100;
            lane.Damage = lane.MoveSpeed = lane.AttackSpeed = lane.Area = lane.DashDistance = lane.Guard = 1;
            lane.Lifesteal = lane.AttackCooldown = lane.Invulnerable = lane.DashRecharge = 0;
            lane.MaxDashes = lane.Dashes = lane.Projectiles = 1;
            lane.Chains = 0; lane.Piercing = false;
            lane.Cyclone = lane.MainSkill == CarnivalMainSkill.Cyclone;
            lane.AutonomousTime = lane.AutonomousCooldown = lane.BarrageCooldown = 0;
            lane.BarragePending = 0;
        }
        phase = CarnivalLanPhase.Combat;
        phaseEnds = now + CarnivalMatchRules.CombatSeconds;
        foreach (var lane in lanes) SpawnStage(lane);
        Debug.Log("LAN match started");
    }

    void OfferCards(Lane lane)
    {
        var offered = CarnivalSkillCatalog.Offer((int)lane.Weapon, lane.MainSkill,
            lane.Supports, random);
        for (int i = 0; i < lane.Cards.Length; i++) lane.Cards[i] = (byte)offered[i];
    }

    void ProcessAction(int slot, CarnivalLanInput input)
    {
        Lane lane = lanes[slot];
        if (input.Action == CarnivalLanAction.ChooseCard && phase == CarnivalLanPhase.Draft && input.ActionValue < 3)
        { lane.ChosenCard = input.ActionValue; return; }
        if (phase != CarnivalLanPhase.Combat || lane.Match.IsRespawning) return;
        if (input.Action == CarnivalLanAction.Dash && lane.Dashes > 0)
        {
            float x = input.MoveX, y = input.MoveY;
            if (x * x + y * y < .01f) { x = input.AimX; y = input.AimY; }
            lane.Combat.DashPlayer(x, y, 1.65f * lane.DashDistance, .55f);
            lane.Dashes--;
            if (lane.DashRecharge <= 0) lane.DashRecharge = 2f;
            lane.Invulnerable = Math.Max(lane.Invulnerable, .3f);
        }
        else if (input.Action == CarnivalLanAction.Summon && input.ActionValue < 4 && lanes[1 - slot] != null)
        {
            int kindIndex = input.ActionValue;
            if (lane.Match.TrySpendCp(SummonCosts[kindIndex]))
                SpawnMonster(lanes[1 - slot], SummonKinds[kindIndex], true);
        }
        else if (input.Action == CarnivalLanAction.Shockwave &&
            lane.Match.TrySpendCp(CarnivalMatchRules.ShockwaveCost))
        {
            foreach (var monster in lane.Combat.Monsters)
            {
                float dx = monster.X - lane.Combat.PlayerX, dy = monster.Y - lane.Combat.PlayerY;
                if (dx * dx + dy * dy > 2.6f * 2.6f * lane.Area * lane.Area) continue;
                lane.Combat.DamageMonster(monster, 3f * lane.Damage, lane.MaxHealth, lane.Lifesteal);
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance > .001f)
                {
                    monster.X += dx / distance * .65f;
                    monster.Y += dy / distance * .65f;
                    lane.Combat.Clamp(ref monster.X, ref monster.Y, monster.Radius);
                }
            }
            for (int i = lane.Combat.Projectiles.Count - 1; i >= 0; i--)
            {
                var shot = lane.Combat.Projectiles[i];
                float dx = shot.X - lane.Combat.PlayerX, dy = shot.Y - lane.Combat.PlayerY;
                if (shot.Hostile && dx * dx + dy * dy < 2.6f * 2.6f) lane.Combat.Remove(shot);
            }
        }
    }

    void Tick(float dt, double now)
    {
        if (phase == CarnivalLanPhase.Waiting || phase == CarnivalLanPhase.Finished) return;
        if (now >= phaseEnds)
        {
            if (phase == CarnivalLanPhase.Draft)
            {
                foreach (var lane in lanes) ApplyCard(lane, lane.Cards[lane.ChosenCard < 0 ? 0 : lane.ChosenCard]);
                phase = CarnivalLanPhase.Combat;
                phaseEnds = now + CarnivalMatchRules.CombatSeconds;
                foreach (var lane in lanes)
                { lane.Match.AdvanceStage(); SpawnStage(lane); }
            }
            else if (lanes[0].Match.Stage >= CarnivalMatchRules.FinalStage) phase = CarnivalLanPhase.Finished;
            else
            {
                phase = CarnivalLanPhase.Draft;
                phaseEnds = now + CarnivalMatchRules.DraftSeconds;
                foreach (var lane in lanes)
                { lane.ChosenCard = -1; OfferCards(lane); lane.Combat.ClearEntities();
                    lane.BarragePending = 0; lane.AutonomousTime = 0; }
            }
            return;
        }
        if (phase != CarnivalLanPhase.Combat) return;
        foreach (var lane in lanes) TickLane(lane, dt, now);
        if (lanes[0].Match.Stage == CarnivalMatchRules.FinalStage &&
            (BossDefeated(lanes[0]) || BossDefeated(lanes[1]))) phase = CarnivalLanPhase.Finished;
    }

    bool BossDefeated(Lane lane)
    {
        foreach (var monster in lane.Combat.Monsters)
            if (monster.Kind == CarnivalMonsterKind.Boss && monster.Health > 0) return false;
        return true;
    }

    void SpawnStage(Lane lane)
    {
        lane.Combat.ClearEntities();
        lane.Combat.PlayerHealth = Math.Min(lane.MaxHealth, lane.Combat.PlayerHealth + 15);
        int count = CarnivalMatchRules.InitialMonsterCount(lane.Match.Stage);
        for (int i = 0; i < count; i++)
            SpawnMonster(lane, CarnivalMatchRules.MonsterForStage(lane.Match.Stage, i), false);
    }

    void SpawnMonster(Lane lane, CarnivalMonsterKind kind, bool summoned)
    {
        CarnivalMonsterRarity rarity = kind == CarnivalMonsterKind.Boss ? CarnivalMonsterRarity.Normal :
            CarnivalMatchRules.RollRarity(lane.Match.Stage, random);
        CarnivalMonsterModifier modifiers = kind == CarnivalMonsterKind.Boss ? CarnivalMonsterModifier.None :
            CarnivalMatchRules.RollModifiers(rarity, random);
        float radius = CarnivalMatchRules.MonsterRadius(kind);
        float x = 0, y = kind == CarnivalMonsterKind.Boss ? 5 : 0;
        if (kind != CarnivalMonsterKind.Boss)
            for (int attempt = 0; attempt < 60; attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                float distance = summoned ? 5f + (float)random.NextDouble() * 2 : 5f + (float)random.NextDouble() * 5;
                x = lane.Combat.PlayerX + (float)Math.Cos(angle) * distance;
                y = lane.Combat.PlayerY + (float)Math.Sin(angle) * distance;
                lane.Combat.Clamp(ref x, ref y, radius);
                bool clear = (x - lane.Combat.PlayerX) * (x - lane.Combat.PlayerX) +
                    (y - lane.Combat.PlayerY) * (y - lane.Combat.PlayerY) > (radius + .75f) * (radius + .75f);
                foreach (var other in lane.Combat.Monsters)
                {
                    float dx = x - other.X, dy = y - other.Y;
                    float gap = radius + other.Radius + .1f;
                    if (dx * dx + dy * dy < gap * gap) { clear = false; break; }
                }
                if (clear) break;
            }
        lane.Combat.Add(new CarnivalMonsterState {
            Kind = kind, Rarity = rarity, Modifiers = modifiers,
            Action = CarnivalMonsterAction.Moving, X = x, Y = y,
            Radius = radius, SpacingRadius = radius,
            Health = CarnivalMatchRules.ModifiedHealth(CarnivalMatchRules.MonsterHealth(kind, lane.Match.Stage), rarity, modifiers),
            MaxHealth = CarnivalMatchRules.ModifiedHealth(CarnivalMatchRules.MonsterHealth(kind, lane.Match.Stage), rarity, modifiers),
            Speed = CarnivalMatchRules.ModifiedSpeed(CarnivalMatchRules.MonsterSpeed(kind), modifiers),
            Damage = CarnivalMatchRules.ModifiedDamage(CarnivalMatchRules.MonsterDamage(kind), modifiers),
            Cooldown = .5f + (float)random.NextDouble() * .9f, Phase = (float)random.NextDouble() * 8
        });
    }

    void TickLane(Lane lane, float dt, double now)
    {
        var combat = lane.Combat;
        lane.AttackCooldown = Math.Max(0, lane.AttackCooldown - dt);
        lane.Invulnerable = Math.Max(0, lane.Invulnerable - dt);
        if (lane.Dashes < lane.MaxDashes)
        {
            lane.DashRecharge -= dt;
            if (lane.DashRecharge <= 0)
            { lane.Dashes++; lane.DashRecharge = lane.Dashes < lane.MaxDashes ? 2f : 0; }
        }
        if (lane.Match.RespawnReady(now))
        { lane.Match.FinishRespawn(); combat.PlayerHealth = lane.MaxHealth;
            combat.PlayerX = combat.PlayerY = 0; lane.Invulnerable = 2; }
        if (!lane.Match.IsRespawning)
        {
            combat.MovePlayer(lane.Input.MoveX, lane.Input.MoveY, 3.8f * lane.MoveSpeed, dt, .55f);
            if (lane.Input.Attack && lane.AttackCooldown <= 0) Attack(lane);
            if (lane.BarragePending > 0)
            {
                lane.BarrageCooldown -= dt;
                if (lane.BarrageCooldown <= 0)
                { Attack(lane, false, true); lane.BarragePending--; lane.BarrageCooldown = .08f; }
            }
            if (lane.AutonomousTime > 0)
            {
                lane.AutonomousTime -= dt;
                lane.AutonomousCooldown -= dt;
                if (lane.AutonomousCooldown <= 0)
                {
                    if (lane.Weapon == CarnivalLanWeapon.Bow)
                    { lane.AutonomousX = combat.PlayerX; lane.AutonomousY = combat.PlayerY; }
                    Attack(lane, true); lane.AutonomousCooldown = .8f;
                }
            }
        }
        combat.TickBleeds(dt);
        foreach (var monster in combat.Monsters)
        {
            if (monster.Health <= 0) continue;
            if (!combat.AdvanceMonster(monster, dt, (float)now)) continue;
            var hit = combat.ResolveMonsterAttack(monster, .55f);
            if (hit.DirectDamage > 0) Hurt(lane, hit.DirectDamage, now);
            if (hit.SpawnProjectile)
                combat.Add(new CarnivalProjectileState { X = hit.ProjectileX, Y = hit.ProjectileY,
                    VelocityX = hit.VelocityX, VelocityY = hit.VelocityY, Radius = hit.ProjectileRadius,
                    Life = hit.ProjectileLife, Damage = hit.ProjectileDamage, Hostile = true });
        }
        combat.ResolveContacts(.55f, lane.Match.IsRespawning);
        UpdateShots(lane, dt, now);
        for (int i = combat.Monsters.Count - 1; i >= 0; i--)
        {
            var monster = combat.Monsters[i];
            if (monster.Health > 0) continue;
            lane.Match.RegisterKill(monster.Kind, monster.Rarity);
            combat.Remove(monster);
        }
        if (combat.Monsters.Count == 0 && lane.Match.Stage < CarnivalMatchRules.FinalStage)
        {
            lane.Match.ScheduleWave(now);
            if (lane.Match.WaveReady(now))
            { for (int i = 0; i < CarnivalMatchRules.InitialMonsterCount(lane.Match.Stage); i++)
                  SpawnMonster(lane, CarnivalMatchRules.MonsterForStage(lane.Match.Stage, i), false);
              lane.Match.ClearWaveSchedule(); }
        }
        else lane.Match.ClearWaveSchedule();
    }

    void Attack(Lane lane, bool autonomous = false, bool barrageFollowup = false)
    {
        var combat = lane.Combat;
        float attackDamage = CarnivalSkillCatalog.RollDamage(lane.MainSkill, random,
            lane.Supports.Contains(CarnivalSupportSkill.Volatility)) * lane.Damage;
        if (autonomous) attackDamage *= .45f;
        if (barrageFollowup) attackDamage *= .65f;
        float aimX = lane.Input.AimX, aimY = lane.Input.AimY;
        float length = (float)Math.Sqrt(aimX * aimX + aimY * aimY);
        if (length < .001f) { aimX = 0; aimY = 1; }
        else { aimX /= length; aimY /= length; }
        float originX = autonomous ? lane.AutonomousX : combat.PlayerX;
        float originY = autonomous ? lane.AutonomousY : combat.PlayerY;
        if (!autonomous && !barrageFollowup && (lane.Weapon == CarnivalLanWeapon.Bow &&
            lane.Supports.Contains(CarnivalSupportSkill.MirageArcher) ||
            lane.Weapon == CarnivalLanWeapon.Wand && lane.Supports.Contains(CarnivalSupportSkill.SpellTotem)))
        {
            lane.AutonomousTime = 3f; lane.AutonomousCooldown = .8f;
            lane.AutonomousX = combat.PlayerX + aimX * 1.3f;
            lane.AutonomousY = combat.PlayerY + aimY * 1.3f;
        }
        if (lane.Weapon == CarnivalLanWeapon.Sword)
        {
            if (lane.MainSkill == CarnivalMainSkill.Flicker)
            {
                CarnivalMonsterState closest = null;
                float best = CarnivalSkillCatalog.FlickerRange * CarnivalSkillCatalog.FlickerRange;
                foreach (var monster in combat.Monsters)
                {
                    float dx = monster.X - combat.PlayerX, dy = monster.Y - combat.PlayerY;
                    float distance = dx * dx + dy * dy;
                    if (monster.Health > 0 && distance < best)
                    { best = distance; closest = monster; }
                }
                if (closest == null) { lane.AttackCooldown = .62f / lane.AttackSpeed; return; }
                float tx = closest.X - combat.PlayerX, ty = closest.Y - combat.PlayerY;
                float travel = Math.Max(0, (float)Math.Sqrt(best) - closest.Radius - .65f);
                combat.DashPlayer(tx, ty, travel, .55f);
                tx = closest.X - combat.PlayerX; ty = closest.Y - combat.PlayerY;
                float newLength = (float)Math.Sqrt(tx * tx + ty * ty);
                if (newLength > .001f) { aimX = tx / newLength; aimY = ty / newLength; }
            }
            bool cyclone = lane.Cyclone;
            var directHits = new List<CarnivalMonsterState>();
            foreach (var monster in combat.Monsters)
            {
                if (monster.Health <= 0) continue;
                bool inRange = cyclone ?
                    (monster.X - combat.PlayerX) * (monster.X - combat.PlayerX) +
                    (monster.Y - combat.PlayerY) * (monster.Y - combat.PlayerY) <=
                    (1.5f * lane.Area + monster.Radius) * (1.5f * lane.Area + monster.Radius) :
                    CarnivalCombatGeometry.InSector(combat.PlayerX, combat.PlayerY, aimX, aimY,
                        monster.X, monster.Y, 2.15f * lane.Area + monster.Radius, 105f);
                if (!inRange) continue;
                int repeats = lane.Supports.Contains(CarnivalSupportSkill.Multistrike) ? 2 : 1;
                for (int i = 0; i < repeats; i++)
                    combat.DamageMonster(monster, attackDamage * (repeats == 1 ? 1f : .65f),
                        lane.MaxHealth, lane.Lifesteal);
                if (lane.Supports.Contains(CarnivalSupportSkill.Bleed)) combat.ApplyBleed(monster);
                if (lane.Supports.Contains(CarnivalSupportSkill.Shockwave))
                    HitCircle(lane, monster.X, monster.Y, monster.Radius + .7f, attackDamage * .35f);
                directHits.Add(monster);
            }
            if (lane.Chains > 0)
            {
                var directIds = new HashSet<int>();
                var shockedIds = new HashSet<int>();
                foreach (var hit in directHits) directIds.Add(hit.Id);
                foreach (var source in directHits)
                    foreach (var target in combat.Monsters)
                    {
                        if (target.Health <= 0 || directIds.Contains(target.Id) || shockedIds.Contains(target.Id)) continue;
                        float dx = source.X - target.X, dy = source.Y - target.Y;
                        float reach = source.Radius + target.Radius + .3f;
                        if (dx * dx + dy * dy > reach * reach) continue;
                        combat.DamageMonster(target, attackDamage * .5f,
                            lane.MaxHealth, lane.Lifesteal);
                        shockedIds.Add(target.Id);
                    }
            }
            if (lane.MainSkill == CarnivalMainSkill.Smite)
                HitCircle(lane, combat.PlayerX + aimX * 1.8f, combat.PlayerY + aimY * 1.8f,
                    1.35f * lane.Area, attackDamage * .45f);
            lane.AttackCooldown = (cyclone ? .18f : lane.MainSkill == CarnivalMainSkill.Flicker ? .62f : .48f) / lane.AttackSpeed;
            return;
        }
        if (lane.MainSkill == CarnivalMainSkill.IceNova)
        {
            HitCircle(lane, originX, originY, 2.2f * lane.Area, attackDamage);
            if (lane.Supports.Contains(CarnivalSupportSkill.SpellEcho))
                HitCircle(lane, originX, originY, 2.2f * lane.Area, attackDamage * .55f);
            if (lane.Supports.Contains(CarnivalSupportSkill.SpellSequence))
            {
                HitCircle(lane, originX + aimX * 1.2f, originY + aimY * 1.2f,
                    2.2f * lane.Area, attackDamage * .35f);
                HitCircle(lane, originX + aimX * 2.4f, originY + aimY * 2.4f,
                    2.2f * lane.Area, attackDamage * .35f);
            }
            if (!autonomous) lane.AttackCooldown = .6f / lane.AttackSpeed;
            return;
        }
        float speed = lane.Weapon == CarnivalLanWeapon.Bow ? 12f : 6.5f;
        float radius = (lane.Weapon == CarnivalLanWeapon.Bow ? .18f : .24f) * lane.Area;
        float life = lane.Weapon == CarnivalLanWeapon.Bow ? 1.5f : 2.1f;
        if (lane.MainSkill == CarnivalMainSkill.Spark)
        { speed = 7.5f; radius = .16f * lane.Area; life = 2.8f; attackDamage *= .55f; }
        else if (lane.MainSkill == CarnivalMainSkill.FreezingPulse)
        { speed = 7.5f; radius = .34f * lane.Area; life = 1.5f; }
        int count = lane.MainSkill == CarnivalMainSkill.Spark ? lane.Projectiles + 3 :
            lane.MainSkill == CarnivalMainSkill.GalvanicArrow ?
                lane.Projectiles + CarnivalSkillCatalog.GalvanicBaseProjectiles - 1 : lane.Projectiles;
        float damage = CarnivalMatchRules.ProjectileDamage(
            attackDamage,
            lane.MainSkill == CarnivalMainSkill.Spark ? count : lane.Projectiles, lane.Piercing);
        bool barrageInitial = !autonomous && !barrageFollowup &&
            lane.Weapon == CarnivalLanWeapon.Bow && lane.Supports.Contains(CarnivalSupportSkill.Barrage);
        if (barrageInitial) damage *= .65f;
        if (lane.MainSkill == CarnivalMainSkill.GalvanicArrow)
        {
            damage *= CarnivalSkillCatalog.GalvanicDamageShare;
            if (!barrageFollowup)
            {
                float reach = CarnivalSkillCatalog.GalvanicConeReach * lane.Area;
                foreach (var monster in combat.Monsters)
                    if (monster.Health > 0 && CarnivalCombatGeometry.InSector(
                        originX, originY, aimX, aimY, monster.X, monster.Y,
                        reach + monster.Radius, CarnivalSkillCatalog.GalvanicConeDegrees))
                        combat.DamageMonster(monster, damage, lane.MaxHealth, lane.Lifesteal);
            }
        }
        if (barrageInitial)
        { lane.BarragePending = 2; lane.BarrageCooldown = .08f; }
        for (int i = 0; i < count; i++)
        {
            float angle = lane.MainSkill == CarnivalMainSkill.GalvanicArrow ?
                ((float)random.NextDouble() - .5f) * CarnivalSkillCatalog.GalvanicConeDegrees * Mathf.Deg2Rad :
                (i - (count - 1) * .5f) * 9f * Mathf.Deg2Rad;
            if (lane.MainSkill != CarnivalMainSkill.GalvanicArrow &&
                lane.Weapon == CarnivalLanWeapon.Bow && lane.Supports.Contains(CarnivalSupportSkill.Nimis))
                angle += ((float)random.NextDouble() * 120f - 60f) * Mathf.Deg2Rad;
            float x = aimX * Mathf.Cos(angle) - aimY * Mathf.Sin(angle);
            float y = aimX * Mathf.Sin(angle) + aimY * Mathf.Cos(angle);
            combat.Add(new CarnivalProjectileState { X = originX + x * .55f,
                Y = originY + y * .55f, VelocityX = x * speed, VelocityY = y * speed,
                Radius = radius, Life = life, Damage = damage,
                Piercing = lane.Piercing || lane.MainSkill == CarnivalMainSkill.FreezingPulse,
                ChainsRemaining = lane.Chains + CarnivalSkillCatalog.BaseChainCount(lane.MainSkill),
                SplitOnImpact = lane.MainSkill == CarnivalMainSkill.TornadoShot,
                RangeRemaining = lane.MainSkill == CarnivalMainSkill.TornadoShot ?
                    CarnivalSkillCatalog.TornadoShotRange - .55f : 0f,
                BouncesRemaining = lane.MainSkill == CarnivalMainSkill.Spark ? 2 : 0,
                Returning = lane.Weapon == CarnivalLanWeapon.Bow && lane.Supports.Contains(CarnivalSupportSkill.Nimis) });
            if (lane.Weapon == CarnivalLanWeapon.Wand)
            {
                int bounces = lane.MainSkill == CarnivalMainSkill.Spark ? 2 : 0;
                if (lane.Supports.Contains(CarnivalSupportSkill.SpellEcho))
                    combat.Add(new CarnivalProjectileState { X = originX + x * .55f, Y = originY + y * .55f,
                        VelocityX = x * speed, VelocityY = y * speed, Radius = radius, Life = life,
                        Damage = damage * .55f, BouncesRemaining = bounces });
                if (lane.Supports.Contains(CarnivalSupportSkill.SpellSequence))
                    for (int copy = 1; copy <= 2; copy++)
                        combat.Add(new CarnivalProjectileState { X = originX + aimX * copy * 1.2f + x * .55f,
                            Y = originY + aimY * copy * 1.2f + y * .55f,
                            VelocityX = x * speed, VelocityY = y * speed, Radius = radius,
                            Life = life, Damage = damage * .35f, BouncesRemaining = bounces });
            }
        }
        if (!autonomous && !barrageFollowup)
            lane.AttackCooldown = (lane.Weapon == CarnivalLanWeapon.Bow ? .42f : .6f) / lane.AttackSpeed;
    }

    void HitCircle(Lane lane, float x, float y, float radius, float damage)
    {
        foreach (var monster in lane.Combat.Monsters)
        {
            float dx = monster.X - x, dy = monster.Y - y;
            float reach = radius + monster.Radius;
            if (monster.Health > 0 && dx * dx + dy * dy <= reach * reach)
            {
                lane.Combat.DamageMonster(monster, damage, lane.MaxHealth, lane.Lifesteal);
                if (lane.MainSkill == CarnivalMainSkill.IceNova) lane.Combat.ApplyChill(monster);
            }
        }
    }

    void UpdateShots(Lane lane, float dt, double now)
    {
        var combat = lane.Combat;
        for (int i = combat.Projectiles.Count - 1; i >= 0; i--)
        {
            var shot = combat.Projectiles[i];
            float beforeX = shot.X, beforeY = shot.Y;
            bool spent = combat.AdvanceProjectile(shot, dt);
            if (spent && !shot.Hostile && shot.BouncesRemaining > 0 && shot.Life > 0)
            {
                shot.BouncesRemaining--;
                shot.X = beforeX; shot.Y = beforeY;
                shot.VelocityX = -shot.VelocityX; shot.VelocityY = -shot.VelocityY;
                spent = false;
            }
            if (spent && !shot.Hostile && shot.Returning && !shot.SplitOnImpact)
            {
                ReturnShot(lane, shot);
                spent = false;
            }
            if (!spent && shot.Hostile)
            {
                float along;
                if (!lane.Match.IsRespawning && CarnivalCombatGeometry.DistanceToSegment(
                    combat.PlayerX, combat.PlayerY, beforeX, beforeY, shot.X, shot.Y, out along) < shot.Radius + .55f)
                { Hurt(lane, shot.Damage, now); spent = true; }
            }
            else if (!spent)
            {
                for (int contact = 0; contact < 8 && !spent; contact++)
                {
                    var hit = combat.FirstProjectileHit(shot, beforeX, beforeY);
                    if (hit == null) break;
                    if (shot.SplitOnImpact)
                    {
                        float along;
                        CarnivalCombatGeometry.DistanceToSegment(hit.X, hit.Y,
                            beforeX, beforeY, shot.X, shot.Y, out along);
                        shot.X = beforeX + (shot.X - beforeX) * along;
                        shot.Y = beforeY + (shot.Y - beforeY) * along;
                    }
                    shot.HitMonsterIds.Add(hit.Id);
                    combat.DamageMonster(hit, shot.Damage, lane.MaxHealth, lane.Lifesteal);
                    if (lane.MainSkill == CarnivalMainSkill.FreezingPulse) combat.ApplyChill(hit);
                    if (shot.SplitOnImpact) { spent = true; break; }
                    var next = shot.ChainsRemaining > 0 ? combat.ChainTarget(shot, hit) : null;
                    if (next != null)
                    {
                        shot.ChainsRemaining--;
                        shot.X = hit.X; shot.Y = hit.Y;
                        float dx = next.X - hit.X, dy = next.Y - hit.Y;
                        float length = (float)Math.Sqrt(dx * dx + dy * dy);
                        float speed = (float)Math.Sqrt(shot.VelocityX * shot.VelocityX + shot.VelocityY * shot.VelocityY);
                        if (length > .001f) { shot.VelocityX = dx / length * speed; shot.VelocityY = dy / length * speed; }
                        break;
                    }
                    if (!shot.Piercing) spent = true;
                }
            }
            if (!spent && shot.SplitOnImpact && shot.RangeRemaining <= 0) spent = true;
            if (spent && shot.Returning && !shot.Hostile && !shot.SplitOnImpact)
            { ReturnShot(lane, shot); spent = false; }
            if (spent && shot.SplitOnImpact && !shot.Hostile)
                SplitTornadoShot(lane, shot);
            if (spent) combat.Remove(shot);
        }
    }

    void ReturnShot(Lane lane, CarnivalProjectileState shot)
    {
        shot.Returning = false;
        float dx = lane.Combat.PlayerX - shot.X, dy = lane.Combat.PlayerY - shot.Y;
        float length = (float)Math.Sqrt(dx * dx + dy * dy);
        if (length > .001f)
        { shot.VelocityX = dx / length * 12f; shot.VelocityY = dy / length * 12f; }
        else { shot.VelocityX = -shot.VelocityX; shot.VelocityY = -shot.VelocityY; }
        shot.Life = .8f;
    }

    void SplitTornadoShot(Lane lane, CarnivalProjectileState primary)
    {
        float speed = (float)Math.Sqrt(primary.VelocityX * primary.VelocityX + primary.VelocityY * primary.VelocityY);
        for (int i = -1; i <= 1; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
            lane.Combat.Add(new CarnivalProjectileState { X = primary.X + x * .2f,
                Y = primary.Y + y * .2f, VelocityX = x * speed, VelocityY = y * speed,
                Life = .5f, Radius = primary.Radius * .8f, Damage = primary.Damage * .5f,
                Piercing = primary.Piercing, ChainsRemaining = primary.ChainsRemaining });
        }
    }

    void Hurt(Lane lane, float damage, double now)
    {
        if (lane.Match.IsRespawning || lane.Invulnerable > 0) return;
        lane.Combat.PlayerHealth = Math.Max(0,
            lane.Combat.PlayerHealth - CarnivalMatchRules.DamageAfterGuard(damage, lane.Guard));
        lane.Invulnerable = .65f;
        if (lane.Combat.PlayerHealth <= 0) lane.Match.BeginRespawn(now);
    }

    void ApplyCard(Lane lane, byte card)
    {
        var support = (CarnivalSupportSkill)card;
        if (!CarnivalSkillCatalog.IsCompatible((int)lane.Weapon, lane.MainSkill, support) ||
            !lane.Supports.Add(support)) return;
        switch (support)
        {
            case CarnivalSupportSkill.Chain:
                lane.Chains += CarnivalSkillCatalog.ChainSupportBonus;
                break;
            case CarnivalSupportSkill.IncreasedArea:
                lane.Area *= 1.3f;
                break;
            case CarnivalSupportSkill.Pierce:
                lane.Piercing = true;
                break;
            case CarnivalSupportSkill.MultipleProjectiles:
                lane.Projectiles += 2;
                break;
            case CarnivalSupportSkill.AddedAttackDamage:
                lane.Damage *= CarnivalSkillCatalog.DefaultDamageMultiplier;
                break;
            case CarnivalSupportSkill.AttackSpeed:
                lane.AttackSpeed *= CarnivalSkillCatalog.DefaultSpeedMultiplier;
                break;
            case CarnivalSupportSkill.LifeLeech:
                lane.Lifesteal += CarnivalSkillCatalog.DefaultLifeLeech;
                break;
            case CarnivalSupportSkill.MaxHealth:
                lane.MaxHealth += CarnivalSkillCatalog.DefaultMaxHealth;
                lane.Combat.PlayerHealth += CarnivalSkillCatalog.DefaultMaxHealth;
                break;
        }
    }

    void SendSnapshot(int slot, double now)
    {
        Lane lane = lanes[slot];
        if (lane == null || socket == null) return;
        Lane opponent = lanes[1 - slot];
        var snapshot = new CarnivalLanSnapshot {
            Slot = (byte)slot, Stage = (byte)lane.Match.Stage, Phase = phase,
            SecondsLeft = (float)Math.Max(0, phaseEnds - now),
            X = lane.Combat.PlayerX, Y = lane.Combat.PlayerY,
            Health = lane.Combat.PlayerHealth, MaxHealth = lane.MaxHealth,
            EnemyHealth = opponent == null ? 0 : opponent.Combat.PlayerHealth,
            Cp = lane.Match.Cp, Score = lane.Match.Score,
            EnemyScore = opponent == null ? 0 : opponent.Match.Score,
            AckActionId = lane.LastActionId, Respawning = lane.Match.IsRespawning,
            Cards = (byte[])lane.Cards.Clone()
        };
        var monsters = new List<CarnivalLanMonsterView>();
        foreach (var m in lane.Combat.Monsters)
            monsters.Add(new CarnivalLanMonsterView { Id = m.Id, Kind = (byte)m.Kind,
                Rarity = (byte)m.Rarity, Action = (byte)m.Action,
                X = m.X, Y = m.Y, HealthRatio = m.MaxHealth <= 0 ? 0 : Math.Max(0, m.Health / m.MaxHealth) });
        snapshot.Monsters = monsters.ToArray();
        var shots = new List<CarnivalLanShotView>();
        foreach (var shot in lane.Combat.Projectiles)
            shots.Add(new CarnivalLanShotView { Id = shot.Id, Hostile = shot.Hostile,
                X = shot.X, Y = shot.Y, Radius = shot.Radius });
        snapshot.Shots = shots.ToArray();
        byte[] bytes = CarnivalLanProtocol.SnapshotPacket(snapshot);
        try { socket.Send(bytes, bytes.Length, lane.Endpoint); }
        catch (SocketException error) { Debug.LogWarning("LAN send: " + error.Message); }
    }
}
