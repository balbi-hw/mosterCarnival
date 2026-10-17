using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CarnivalGame : MonoBehaviour
{
    enum State { Menu, VersusLobby, WeaponChoice, SkillChoice, Playing, UpgradeChoice, Lost, Won }
    enum Weapon { Sword, Bow, Wand }

    class Monster
    {
        public readonly CarnivalMonsterState model = new CarnivalMonsterState();
        public CarnivalMonsterKind kind { get => model.Kind; set => model.Kind = value; }
        public CarnivalMonsterRarity rarity { get => model.Rarity; set => model.Rarity = value; }
        public CarnivalMonsterAction action { get => model.Action; set => model.Action = value; }
        public CarnivalMonsterAction displayedAction;
        public Vector2 position { get => new Vector2(model.X, model.Y); set { model.X = value.x; model.Y = value.y; } }
        public Vector2 attackDirection { get => new Vector2(model.AttackX, model.AttackY);
            set { model.AttackX = value.x; model.AttackY = value.y; } }
        public Transform body, shadow, rarityRing, healthBar, warning;
        public float visualHeight;
        public float health { get => model.Health; set => model.Health = value; }
        public float maxHealth { get => model.MaxHealth; set => model.MaxHealth = value; }
        public float radius { get => model.Radius; set => model.Radius = value; }
        public float spacingRadius { get => model.SpacingRadius; set => model.SpacingRadius = value; }
        public float speed { get => model.Speed; set => model.Speed = value; }
        public float damage { get => model.Damage; set => model.Damage = value; }
        public float cooldown { get => model.Cooldown; set => model.Cooldown = value; }
        public float actionTimer { get => model.ActionTimer; set => model.ActionTimer = value; }
        public float phase { get => model.Phase; set => model.Phase = value; }
    }

    class Shot
    {
        public readonly CarnivalProjectileState model = new CarnivalProjectileState();
        public Vector2 position { get => new Vector2(model.X, model.Y); set { model.X = value.x; model.Y = value.y; } }
        public Vector2 velocity { get => new Vector2(model.VelocityX, model.VelocityY);
            set { model.VelocityX = value.x; model.VelocityY = value.y; } }
        public Transform body;
        public float life { get => model.Life; set => model.Life = value; }
        public float radius { get => model.Radius; set => model.Radius = value; }
        public float damage { get => model.Damage; set => model.Damage = value; }
        public bool hostile { get => model.Hostile; set => model.Hostile = value; }
        public bool piercing { get => model.Piercing; set => model.Piercing = value; }
        public int chainsRemaining { get => model.ChainsRemaining; set => model.ChainsRemaining = value; }
    }

    const float ArenaRadius = 15f;
    const float PlayerRadius = .55f;
    readonly CarnivalMatchState match = new CarnivalMatchState();
    readonly CarnivalCombatState combat = new CarnivalCombatState();
    readonly List<Monster> monsters = new List<Monster>();
    readonly List<Shot> shots = new List<Shot>();
    readonly Dictionary<string, Sprite> fallback = new Dictionary<string, Sprite>();
    readonly RectTransform[] summonButtons = new RectTransform[4];
    readonly RectTransform[] weaponButtons = new RectTransform[3];
    readonly Image[] weaponButtonImages = new Image[3];
    readonly Text[] choiceTitles = new Text[3];
    readonly Text[] choiceDescriptions = new Text[3];
    readonly CarnivalSupportSkill[] offeredSupports = new CarnivalSupportSkill[3];
    readonly HashSet<CarnivalSupportSkill> selectedSupports = new HashSet<CarnivalSupportSkill>();
    readonly System.Random skillRandom = new System.Random();
    readonly string[] weaponNames = { "SWORD", "BOW", "WAND" };
    readonly string[] summonNames = { "SWARM", "RANGED", "TANK", "OGRE" };
    readonly CarnivalMonsterKind[] summonKinds = { CarnivalMonsterKind.Skitter, CarnivalMonsterKind.Spitter, CarnivalMonsterKind.Colossus, CarnivalMonsterKind.Ogre };

    Camera viewCamera;
    Transform hero, heroShadow, weaponVisual, slashVisual, cycloneVisual, autonomousVisual;
    Canvas hudCanvas;
    Text statusLabel, bannerLabel, menuTitle, menuBody, menuButtonText;
    GameObject menuShade;
    RectTransform menuButton, versusButton;
    Font uiFont;
    State state = State.Menu;
    Weapon selectedWeapon = Weapon.Sword;
    CarnivalMainSkill selectedMainSkill = CarnivalMainSkill.Cyclone;
    Vector2 heroPosition { get => new Vector2(combat.PlayerX, combat.PlayerY);
        set { combat.PlayerX = value.x; combat.PlayerY = value.y; } }
    float health { get => combat.PlayerHealth; set => combat.PlayerHealth = value; }
    Vector2 aim = Vector2.up;
    Vector2 moveTarget;
    bool hasMoveTarget;
    float maxHealth = 100, damageBonus = 1, moveBonus = 1, dashDistanceBonus = 1,
        areaBonus = 1, attackSpeedBonus = 1, damageTakenBonus = 1;
    float fireCooldown, dashCooldown, invulnerable, bannerTime, slashTime;
    float autonomousTime, autonomousCooldown, barrageCooldown, barrageDamage;
    int barragePending;
    Vector2 autonomousPosition;
    int pendingUpgrade = -1;
    int maxDashes = 1, dashes = 1, projectileCount = 1, chainCount;
    bool cycloneEnabled, piercingEnabled;
    float lifesteal;
    string banner = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (CarnivalLanServer.IsServerMode) return;
        if (CarnivalLanClient.HasAutoConnect)
        {
            new GameObject("Carnival LAN Client").AddComponent<CarnivalLanClient>();
            return;
        }
        if (FindAnyObjectByType<CarnivalGame>() == null)
            new GameObject("Carnival Game Controller").AddComponent<CarnivalGame>();
    }

    void Awake()
    {
        Application.targetFrameRate = 60;
        var pillarX = new float[CarnivalArena3D.Pillars.Length];
        var pillarY = new float[CarnivalArena3D.Pillars.Length];
        for (int i = 0; i < pillarX.Length; i++)
        { pillarX[i] = CarnivalArena3D.Pillars[i].x; pillarY[i] = CarnivalArena3D.Pillars[i].y; }
        combat.SetPillars(pillarX, pillarY, CarnivalArena3D.PillarRadius);
        viewCamera = Camera.main;
        if (viewCamera == null)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            viewCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }
        viewCamera.orthographic = true;
        viewCamera.orthographicSize = 5.5f;
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = new Color(.055f, .08f, .12f);
        var floor = GameObject.Find("ArenaFloor");
        if (floor != null)
        {
            floor.transform.position = new Vector3(0, -.24f, 0);
            floor.transform.localScale = new Vector3(31, .35f, 31);
        }
        if (GameObject.Find("Arena 3D") == null)
        {
            Material source = floor != null ? floor.GetComponent<Renderer>().sharedMaterial : null;
            CarnivalArena3D.Build(floor, CarnivalArena3D.MakePalette(source));
        }
        EnsureFallbackSprites();
        BuildHud();
        UpdateCamera();
    }

    void EnsureFallbackSprites()
    {
        if (fallback.Count > 0) return;
        fallback["square"] = MakeSprite(false);
        fallback["circle"] = MakeSprite(true);
    }

    Sprite MakeSprite(bool circle)
    {
        const int width = 32;
        var texture = new Texture2D(width, width, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        var pixels = new Color[width * width];
        for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
            {
                float dx = x - 15.5f, dy = y - 15.5f;
                pixels[y * width + x] = !circle || dx * dx + dy * dy < 15.5f * 15.5f ? Color.white : Color.clear;
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, width, width), new Vector2(.5f, .5f), width);
    }

    Sprite MakeWeaponSprite(Weapon weapon, bool arrow = false)
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color color = Color.clear;
                if (arrow)
                {
                    if (Mathf.Abs(x - 16) <= 1 && y >= 3 && y <= 24) color = new Color(.65f, .38f, .17f);
                    if (y >= 23 && y <= 29 && Mathf.Abs(x - 16) <= (29 - y) / 2 + 1)
                        color = new Color(.93f, .95f, 1f);
                }
                else if (weapon == Weapon.Sword)
                {
                    if (Mathf.Abs(x - 16) <= 2 && y >= 12 && y <= 28)
                        color = new Color(.88f, .95f, 1f);
                    if (y >= 27 && Mathf.Abs(x - 16) <= 29 - y) color = Color.white;
                    if (y >= 9 && y <= 11 && Mathf.Abs(x - 16) <= 8) color = new Color(.93f, .71f, .26f);
                    if (Mathf.Abs(x - 16) <= 2 && y >= 2 && y <= 8) color = new Color(.49f, .27f, .12f);
                }
                else if (weapon == Weapon.Bow)
                {
                    float curve = 22f - 7f * Mathf.Sin(y * Mathf.PI / 31f);
                    if (y >= 2 && y <= 29 && Mathf.Abs(x - curve) <= 1.7f)
                        color = new Color(.66f, .38f, .16f);
                    if (y >= 3 && y <= 28 && x == 22) color = new Color(.95f, .9f, .67f);
                }
                else
                {
                    if (Mathf.Abs(x - 16) <= 2 && y >= 2 && y <= 24)
                        color = new Color(.46f, .3f, .17f);
                    if ((x - 16) * (x - 16) + (y - 26) * (y - 26) <= 25)
                        color = new Color(.58f, .47f, 1f);
                }
                pixels[y * size + x] = color;
            }
        texture.SetPixels(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
    }

    Vector3 At(Vector2 pos, float height) { return new Vector3(pos.x, height, pos.y); }

    Transform Visual(string name, string art, Vector2 pos, float height, float size, Color color, int order)
    {
        EnsureFallbackSprites();
        var go = new GameObject(name);
        go.transform.position = At(pos, height);
        go.transform.localScale = Vector3.one * size;
        var renderer = go.AddComponent<SpriteRenderer>();
        var loaded = string.IsNullOrEmpty(art) ? null : Resources.Load<Sprite>("CarnivalArt/" + art);
        renderer.sprite = loaded != null ? loaded : fallback["square"];
        renderer.color = loaded != null ? Color.white : color;
        renderer.sortingOrder = order;
        return go.transform;
    }

    Transform Disc(string name, Vector2 pos, float size, Color color, int order)
    {
        var disc = Visual(name, null, pos, .015f, size, color, order);
        var renderer = disc.GetComponent<SpriteRenderer>();
        renderer.sprite = fallback["circle"];
        disc.rotation = Quaternion.Euler(-90, 0, 0);
        return disc;
    }

    void StartGame()
    {
        selectedSupports.Clear();
        EnsureFallbackSprites();
        if (hudCanvas == null || weaponButtons[0] == null || versusButton == null) BuildHud();
        ClearRound();
        heroPosition = Vector2.zero;
        hasMoveTarget = false;
        hero = CarnivalActor3D.Character("Hero", true, heroPosition, 1.15f,
            PlayerRadius, new Color(.54f, .88f, .86f));
        heroShadow = Disc("Hero shadow", heroPosition, PlayerRadius * 2.2f,
            new Color(0, 0, 0, .25f), 0);
        weaponVisual = CarnivalActor3D.Weapon("Equipped " + selectedWeapon, heroPosition,
            (int)selectedWeapon);
        slashVisual = GroundSector("Sword slash", heroPosition, Vector2.up, 2.15f, 105f,
            new Color(.83f, .95f, 1f, .72f));
        slashVisual.gameObject.SetActive(false);
        cycloneVisual = GroundSector("Cyclone channel", heroPosition, Vector2.up, 1.5f, 360f,
            new Color(.35f, .86f, 1f, .42f));
        cycloneVisual.gameObject.SetActive(false);
        autonomousVisual = Disc("Autonomous skill", heroPosition, .85f,
            new Color(.55f, .85f, 1f, .6f), 2);
        autonomousVisual.gameObject.SetActive(false);
        maxHealth = health = 100;
        damageBonus = moveBonus = dashDistanceBonus = areaBonus = attackSpeedBonus = damageTakenBonus = 1;
        maxDashes = dashes = projectileCount = 1;
        chainCount = 0;
        cycloneEnabled = selectedMainSkill == CarnivalMainSkill.Cyclone;
        piercingEnabled = false;
        lifesteal = 0;
        fireCooldown = dashCooldown = invulnerable = 0;
        autonomousTime = autonomousCooldown = barrageCooldown = 0;
        barragePending = 0;
        pendingUpgrade = -1;
        match.ResetForNewMatch(Time.realtimeSinceStartupAsDouble);
        state = State.Playing;
        SpawnStage();
        UpdateCamera();
    }

    void ClearRound()
    {
        if (hero != null) Destroy(hero.gameObject);
        if (heroShadow != null) Destroy(heroShadow.gameObject);
        if (weaponVisual != null) Destroy(weaponVisual.gameObject);
        if (slashVisual != null) Destroy(slashVisual.gameObject);
        if (cycloneVisual != null) Destroy(cycloneVisual.gameObject);
        if (autonomousVisual != null) Destroy(autonomousVisual.gameObject);
        foreach (var m in monsters) DestroyMonsterVisuals(m);
        foreach (var shot in shots) if (shot.body != null) Destroy(shot.body.gameObject);
        monsters.Clear();
        shots.Clear();
        combat.Clear();
    }

    void DestroyMonsterVisuals(Monster m)
    {
        if (m.body != null) Destroy(m.body.gameObject);
        if (m.shadow != null) Destroy(m.shadow.gameObject);
        if (m.rarityRing != null) Destroy(m.rarityRing.gameObject);
        if (m.healthBar != null) Destroy(m.healthBar.gameObject);
        if (m.warning != null) Destroy(m.warning.gameObject);
    }

    void SpawnStage()
    {
        if (match.Stage == CarnivalMatchRules.FinalStage)
        {
            AddMonster(CarnivalMonsterKind.Boss, new Vector2(0, 5));
            Announce("STAGE 5  ·  FINAL BOSS", 3);
            return;
        }
        int count = CarnivalMatchRules.InitialMonsterCount(match.Stage);
        for (int i = 0; i < count; i++)
        {
            CarnivalMonsterKind kind = CarnivalMatchRules.MonsterForStage(match.Stage, i);
            AddMonster(kind, RandomSpawn(5, 10));
        }
        Announce("STAGE " + match.Stage + (match.Stage == 4 ? "  ·  OGRE ARRIVES" : ""), 2.5f);
    }

    Vector2 RandomSpawn(float minDistance, float maxDistance)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector2 pos = heroPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(minDistance, maxDistance);
        return Clamp(pos, .8f);
    }

    Vector2 Clamp(Vector2 pos, float radius)
    {
        float x = pos.x, y = pos.y;
        combat.Clamp(ref x, ref y, radius);
        return new Vector2(x, y);
    }

    Vector2 ClearSpawn(Vector2 requested, float radius, float spacingRadius)
    {
        Vector2 best = Clamp(requested, radius);
        float bestClearance = float.NegativeInfinity;
        for (int i = 0; i < 128; i++)
        {
            float angle = i * 2.399963f;
            float distance = i == 0 ? 0 : .8f * Mathf.Sqrt(i);
            Vector2 candidate = Clamp(requested + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance, radius);
            float clearance = Vector2.Distance(candidate, heroPosition) - spacingRadius - PlayerRadius - .03f;
            foreach (var other in monsters)
                if (other.health > 0)
                    clearance = Mathf.Min(clearance, Vector2.Distance(candidate, other.position) - spacingRadius - other.spacingRadius - .03f);
            if (clearance >= 0) return candidate;
            if (clearance > bestClearance) { best = candidate; bestClearance = clearance; }
        }
        return best;
    }

    void AddMonster(CarnivalMonsterKind kind, Vector2 pos)
    {
        CarnivalMonsterRarity rarity = kind == CarnivalMonsterKind.Boss ? CarnivalMonsterRarity.Normal :
            CarnivalMatchRules.RollRarity(match.Stage, skillRandom);
        CarnivalMonsterModifier modifiers = kind == CarnivalMonsterKind.Boss ? CarnivalMonsterModifier.None :
            CarnivalMatchRules.RollModifiers(rarity, skillRandom);
        float radius = CarnivalMatchRules.MonsterRadius(kind);
        float hp = CarnivalMatchRules.ModifiedHealth(CarnivalMatchRules.MonsterHealth(kind, match.Stage), rarity, modifiers);
        float speed = CarnivalMatchRules.ModifiedSpeed(CarnivalMatchRules.MonsterSpeed(kind), modifiers);
        var color = kind == CarnivalMonsterKind.Skitter ? new Color(.38f, .86f, .43f) :
            kind == CarnivalMonsterKind.Spitter ? new Color(.64f, .43f, .92f) :
            kind == CarnivalMonsterKind.Colossus ? new Color(.43f, .62f, .78f) :
            kind == CarnivalMonsterKind.Ogre ? new Color(.88f, .51f, .25f) : new Color(.82f, .25f, .29f);
        float visualSize = kind == CarnivalMonsterKind.Boss ? 2.1f : kind == CarnivalMonsterKind.Ogre ? 1.75f :
            kind == CarnivalMonsterKind.Colossus ? 1.45f : kind == CarnivalMonsterKind.Spitter ? 1f : .75f;
        float spacingRadius = radius;
        pos = ClearSpawn(pos, radius, spacingRadius);
        var m = new Monster
        {
            kind = kind, rarity = rarity, action = CarnivalMonsterAction.Moving, displayedAction = CarnivalMonsterAction.Moving,
            position = pos, radius = radius, spacingRadius = spacingRadius,
            visualHeight = visualSize, health = hp, maxHealth = hp,
            speed = speed, damage = CarnivalMatchRules.ModifiedDamage(CarnivalMatchRules.MonsterDamage(kind), modifiers),
            cooldown = Random.Range(.5f, 1.4f), phase = Random.Range(0, 8f),
            body = CarnivalActor3D.Character(rarity + " " + kind, false, pos, visualSize, radius,
                RarityBodyColor(color, rarity)),
            shadow = Disc(kind + " shadow", pos, radius * 2.2f, new Color(0, 0, 0, .3f), 0),
            rarityRing = rarity == CarnivalMonsterRarity.Normal ? null :
                Disc(rarity + " marker", pos, radius * 2.8f,
                    rarity == CarnivalMonsterRarity.Rare ? new Color(1f, .74f, .12f, .7f) :
                    new Color(.25f, .45f, 1f, .65f), 1),
            healthBar = Visual(kind + " HP", null, pos, visualSize + .25f, kind == CarnivalMonsterKind.Boss ? 2.2f : .9f,
                rarity == CarnivalMonsterRarity.Rare ? new Color(1f, .75f, .15f) :
                rarity == CarnivalMonsterRarity.Magic ? new Color(.35f, .55f, 1f) :
                new Color(.5f, .95f, .59f), 12)
        };
        m.model.Modifiers = modifiers;
        m.healthBar.localScale = new Vector3(kind == CarnivalMonsterKind.Boss ? 2.2f : .9f, .09f, 1);
        if (kind == CarnivalMonsterKind.Ogre)
        {
            m.warning = GroundSector("Ogre cone", pos, Vector2.up, AttackRange(m), 90f,
                new Color(1f, .28f, .12f, .3f));
            m.warning.gameObject.SetActive(false);
        }
        else if (kind == CarnivalMonsterKind.Boss)
        {
            m.warning = Disc(kind + " attack warning", pos, AttackRange(m) * 2,
                new Color(1, .18f, .1f, .22f), 1);
            m.warning.gameObject.SetActive(false);
        }
        monsters.Add(m);
        combat.Add(m.model);
    }

    void Announce(string text, float seconds)
    {
        banner = text;
        bannerTime = seconds;
    }

    void OfferSupports()
    {
        var offered = CarnivalSkillCatalog.Offer((int)selectedWeapon, selectedMainSkill,
            selectedSupports, skillRandom);
        for (int i = 0; i < offeredSupports.Length; i++) offeredSupports[i] = offered[i];
        pendingUpgrade = -1;
        match.BeginDraft();
        state = State.UpgradeChoice;
    }

    void FinishDraft()
    {
        ChooseSupport(offeredSupports[pendingUpgrade < 0 ? 0 : pendingUpgrade]);
    }

    void ChooseSupport(CarnivalSupportSkill support)
    {
        selectedSupports.Add(support);
        ApplyDefaultSupport(support);
        if (support == CarnivalSupportSkill.Chain)
            chainCount += CarnivalSkillCatalog.ChainSupportBonus;
        if (support == CarnivalSupportSkill.IncreasedArea) areaBonus *= 1.3f;
        if (support == CarnivalSupportSkill.Pierce) piercingEnabled = true;
        if (support == CarnivalSupportSkill.MultipleProjectiles) projectileCount += 2;
        match.AdvanceStage();
        if (!match.IsRespawning) health = Mathf.Min(maxHealth, health + 15);
        foreach (var monster in monsters) DestroyMonsterVisuals(monster);
        monsters.Clear();
        foreach (var shot in shots) if (shot.body != null) Destroy(shot.body.gameObject);
        shots.Clear();
        barragePending = 0;
        autonomousTime = 0;
        combat.ClearEntities();
        state = State.Playing;
        SpawnStage();
    }

    void ApplyDefaultSupport(CarnivalSupportSkill support)
    {
        switch (support)
        {
            case CarnivalSupportSkill.AddedAttackDamage:
                damageBonus *= CarnivalSkillCatalog.DefaultDamageMultiplier;
                break;
            case CarnivalSupportSkill.AttackSpeed:
                attackSpeedBonus *= CarnivalSkillCatalog.DefaultSpeedMultiplier;
                break;
            case CarnivalSupportSkill.LifeLeech:
                lifesteal += CarnivalSkillCatalog.DefaultLifeLeech;
                break;
            case CarnivalSupportSkill.MaxHealth:
                maxHealth += CarnivalSkillCatalog.DefaultMaxHealth;
                health += CarnivalSkillCatalog.DefaultMaxHealth;
                break;
        }
    }

    void SelectChoice(int index)
    {
        if (state == State.WeaponChoice)
        {
            selectedWeapon = (Weapon)index;
            state = State.SkillChoice;
        }
        else if (state == State.SkillChoice)
        {
            selectedMainSkill = CarnivalSkillCatalog.MainForWeapon((int)selectedWeapon, index);
            StartGame();
        }
        else if (state == State.UpgradeChoice && pendingUpgrade < 0)
            pendingUpgrade = index;
    }

    void SelectMenuAction()
    {
        if (state == State.VersusLobby) state = State.Menu;
        else if (state == State.Menu || state == State.Lost || state == State.Won)
            state = State.WeaponChoice;
    }

    void Update()
    {
        if (match.RespawnReady(Time.realtimeSinceStartupAsDouble) &&
            (state == State.Playing || state == State.UpgradeChoice))
            RespawnHero();
        if (state != State.Playing)
        {
            if (state == State.UpgradeChoice && match.PhaseExpired(Time.realtimeSinceStartupAsDouble))
            {
                FinishDraft();
                return;
            }
            bool enter = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            if (state == State.VersusLobby && enter) state = State.Menu;
            else if ((state == State.Menu || state == State.Lost || state == State.Won) && enter)
                state = State.WeaponChoice;
            return;
        }


        if (match.PhaseExpired(Time.realtimeSinceStartupAsDouble))
        {
            if (match.Stage == CarnivalMatchRules.FinalStage) { state = State.Lost; match.EndMatch(); }
            else OfferSupports();
            return;
        }

        float dt = Mathf.Min(Time.deltaTime, .04f);
        fireCooldown = Mathf.Max(0, fireCooldown - dt);
        if (barragePending > 0)
        {
            barrageCooldown -= dt;
            if (barrageCooldown <= 0)
            {
                FireProjectiles(12f, .18f * areaBonus, 1.5f, barrageDamage);
                barragePending--;
                barrageCooldown = .08f;
            }
        }
        if (autonomousTime > 0)
        {
            autonomousTime -= dt;
            autonomousCooldown -= dt;
            if (selectedWeapon == Weapon.Bow) autonomousPosition = heroPosition;
            if (autonomousVisual != null)
            { autonomousVisual.position = At(autonomousPosition, .06f);
                autonomousVisual.gameObject.SetActive(true); }
            if (autonomousCooldown <= 0 && !match.IsRespawning)
            {
                Fire(true); autonomousCooldown = .8f;
            }
        }
        else if (autonomousVisual != null) autonomousVisual.gameObject.SetActive(false);
        slashTime = Mathf.Max(0, slashTime - dt);
        if (slashVisual != null) slashVisual.gameObject.SetActive(slashTime > 0);
        if (cycloneVisual != null)
            cycloneVisual.gameObject.SetActive(!match.IsRespawning && cycloneEnabled && selectedWeapon == Weapon.Sword &&
                Mouse.current != null && Mouse.current.rightButton.isPressed &&
                !PointerOverSummonButton(Mouse.current.position.ReadValue()));
        if (dashes < maxDashes)
        {
            dashCooldown -= dt;
            if (dashCooldown <= 0)
            {
                dashes++;
                dashCooldown = dashes < maxDashes ? 2f : 0f;
            }
        }
        invulnerable = Mathf.Max(0, invulnerable - dt);
        bannerTime = Mathf.Max(0, bannerTime - dt);

        if (!match.IsRespawning)
        {
        var keys = Keyboard.current;
        var mouse = Mouse.current;
        bool onButton = HandleSummonButtons();
        Vector2 move = Vector2.zero;
        if (mouse != null)
        {
            Ray moveRay = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (mouse.leftButton.isPressed && !onButton &&
                new Plane(Vector3.up, Vector3.zero).Raycast(moveRay, out float moveDistance))
            {
                Vector3 hit = moveRay.GetPoint(moveDistance);
                moveTarget = new Vector2(hit.x, hit.z);
                hasMoveTarget = true;
            }
        }
        if (hasMoveTarget)
        {
            Vector2 toward = moveTarget - heroPosition;
            float distance = toward.magnitude;
            if (distance <= .12f) hasMoveTarget = false;
            else move = toward / distance * Mathf.Min(1f, distance / .5f);
        }
        combat.MovePlayer(move.x, move.y, 3.8f * moveBonus, dt, PlayerRadius);
        hero.position = At(heroPosition, 0);
        heroShadow.position = At(heroPosition, .015f);
        if (weaponVisual != null) weaponVisual.position = At(heroPosition + aim * .54f, .58f);
        if (slashVisual != null && slashTime > 0) PlaceGroundAttack(slashVisual, heroPosition, aim);
        UpdateCamera();

        if (mouse != null)
        {
            Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
            {
                Vector3 hit = ray.GetPoint(distance);
                Vector2 toward = new Vector2(hit.x, hit.z) - heroPosition;
                if (toward.sqrMagnitude > .02f) aim = toward.normalized;
            }
            hero.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.y));
        }

        if (keys != null && (keys.leftShiftKey.wasPressedThisFrame || keys.rightShiftKey.wasPressedThisFrame)) Dash(move);
        if (keys != null && keys.eKey.wasPressedThisFrame) Shockwave();
        if (mouse != null && mouse.rightButton.isPressed && !onButton) Fire();
        if (cycloneVisual != null && cycloneVisual.gameObject.activeSelf)
        {
            cycloneVisual.position = At(heroPosition, .055f);
            cycloneVisual.localScale = Vector3.one * areaBonus;
        }
        }

        combat.TickBleeds(dt);
        UpdateMonsters(dt);
        if (state != State.Playing) return;
        UpdateShots(dt);
        if (state != State.Playing) return;
        RemoveDeadMonsters();
        if (monsters.Count == 0)
        {
            if (match.Stage == CarnivalMatchRules.FinalStage) { state = State.Won; return; }
            match.ScheduleWave(Time.realtimeSinceStartupAsDouble);
            if (match.WaveReady(Time.realtimeSinceStartupAsDouble))
            {
                SpawnStage();
                match.ClearWaveSchedule();
            }
        }
        else match.ClearWaveSchedule();
    }

    void OpenVersusLobby()
    {
        if (hudCanvas != null) Destroy(hudCanvas.gameObject);
        new GameObject("Carnival LAN Client").AddComponent<CarnivalLanClient>();
        Destroy(gameObject);
    }

    bool HandleSummonButtons()
    {
        var mouse = Mouse.current;
        if (mouse == null) return false;
        Vector2 cursor = mouse.position.ReadValue();
        for (int i = 0; i < summonButtons.Length; i++)
        {
            if (summonButtons[i] == null || !RectTransformUtility.RectangleContainsScreenPoint(summonButtons[i], cursor)) continue;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                AddMonster(summonKinds[i], RandomSpawn(4.5f, 6.5f));
                Announce("SUMMONED: " + summonNames[i], 1.1f);
            }
            return true;
        }
        return false;
    }

    bool PointerOverSummonButton(Vector2 cursor)
    {
        foreach (var button in summonButtons)
            if (button != null && RectTransformUtility.RectangleContainsScreenPoint(button, cursor))
                return true;
        return false;
    }

    void Dash(Vector2 move)
    {
        if (dashes <= 0) return;
        Vector2 direction = move.sqrMagnitude > .01f ? move : aim;
        combat.DashPlayer(direction.x, direction.y, 1.65f * dashDistanceBonus, PlayerRadius);
        hero.position = At(heroPosition, 0);
        heroShadow.position = At(heroPosition, .015f);
        dashes--;
        if (dashes == maxDashes - 1) dashCooldown = 2;
        invulnerable = .3f;
        UpdateCamera();
    }

    void Fire(bool autonomous = false)
    {
        if (!autonomous && fireCooldown > 0) return;
        float attackDamage = CarnivalSkillCatalog.RollDamage(selectedMainSkill, skillRandom,
            selectedSupports.Contains(CarnivalSupportSkill.Volatility)) * damageBonus;
        if (autonomous) attackDamage *= .45f;
        else if ((selectedWeapon == Weapon.Bow && selectedSupports.Contains(CarnivalSupportSkill.MirageArcher)) ||
            (selectedWeapon == Weapon.Wand && selectedSupports.Contains(CarnivalSupportSkill.SpellTotem)))
        {
            autonomousTime = 3f;
            autonomousCooldown = .8f;
            autonomousPosition = heroPosition + aim * 1.3f;
        }
        if (selectedWeapon == Weapon.Sword)
        {
            if (selectedMainSkill == CarnivalMainSkill.Flicker)
            {
                Monster closest = null;
                float best = CarnivalSkillCatalog.FlickerRange * CarnivalSkillCatalog.FlickerRange;
                foreach (var monster in monsters)
                {
                    float distance = (monster.position - heroPosition).sqrMagnitude;
                    if (monster.health > 0 && distance < best)
                    { best = distance; closest = monster; }
                }
                if (closest != null)
                {
                    Vector2 toward = closest.position - heroPosition;
                    float travel = Mathf.Max(0, toward.magnitude - closest.radius - PlayerRadius - .1f);
                    combat.DashPlayer(toward.x, toward.y, travel, PlayerRadius);
                    hero.position = At(heroPosition, 0);
                    aim = (closest.position - heroPosition).normalized;
                    MeleeHit(false, attackDamage);
                }
                fireCooldown = .62f / attackSpeedBonus;
                return;
            }
            if (cycloneEnabled)
            {
                MeleeHit(true, attackDamage);
                fireCooldown = .18f / attackSpeedBonus;
            }
            else
            {
                MeleeHit(false, attackDamage);
                if (selectedMainSkill == CarnivalMainSkill.Smite)
                {
                    HitCircle(heroPosition + aim * 1.8f, 1.35f * areaBonus, attackDamage * .45f);
                    var effect = GroundSector("Smite lightning", heroPosition + aim * 1.8f,
                        aim, 1.35f * areaBonus, 360f, new Color(.85f, .9f, 1f, .55f));
                    Destroy(effect.gameObject, .16f);
                }
                slashTime = .16f;
                slashVisual.localScale = Vector3.one * areaBonus;
                PlaceGroundAttack(slashVisual, heroPosition, aim);
                slashVisual.gameObject.SetActive(true);
                fireCooldown = .48f / attackSpeedBonus;
            }
        }
        else if (selectedWeapon == Weapon.Bow)
        {
            bool galvanic = selectedMainSkill == CarnivalMainSkill.GalvanicArrow;
            float arrowDamage = galvanic ? attackDamage * CarnivalSkillCatalog.GalvanicDamageShare : attackDamage;
            bool barrage = !autonomous && selectedSupports.Contains(CarnivalSupportSkill.Barrage);
            if (barrage) arrowDamage *= .65f;
            FireProjectiles(12f, .18f * areaBonus, 1.5f, arrowDamage,
                autonomous ? autonomousPosition : heroPosition);
            if (galvanic) GalvanicConeHit(arrowDamage, autonomous ? autonomousPosition : heroPosition);
            if (barrage) { barragePending = 2; barrageDamage = arrowDamage; barrageCooldown = .08f; }
            if (!autonomous) fireCooldown = .42f / attackSpeedBonus;
        }
        else
        {
            Vector2 origin = autonomous ? autonomousPosition : heroPosition;
            CastWandAt(origin, attackDamage);
            if (selectedSupports.Contains(CarnivalSupportSkill.SpellEcho))
                CastWandAt(origin, attackDamage * .55f);
            if (selectedSupports.Contains(CarnivalSupportSkill.SpellSequence))
            {
                CastWandAt(origin + aim * 1.2f, attackDamage * .35f);
                CastWandAt(origin + aim * 2.4f, attackDamage * .35f);
            }
            if (!autonomous) fireCooldown = .6f / attackSpeedBonus;
        }
    }

    void CastWandAt(Vector2 origin, float damage)
    {
        if (selectedMainSkill == CarnivalMainSkill.IceNova)
        {
            HitCircle(origin, 2.2f * areaBonus, damage);
            var effect = GroundSector("Ice nova", origin, Vector2.up,
                2.2f * areaBonus, 360f, new Color(.4f, .85f, 1f, .5f));
            Destroy(effect.gameObject, .18f);
            return;
        }
        if (selectedMainSkill == CarnivalMainSkill.Spark)
        {
            int previous = projectileCount;
            projectileCount = previous + 3;
            FireProjectiles(7.5f, .16f * areaBonus, 2.8f, damage * .55f, origin);
            projectileCount = previous;
        }
        else FireProjectiles(7.5f, .34f * areaBonus, 1.5f, damage, origin);
    }

    void HitCircle(Vector2 center, float radius, float damage)
    {
        foreach (var monster in monsters)
            if (monster.health > 0 && Vector2.Distance(center, monster.position) <= radius + monster.radius)
            {
                DamageMonster(monster, damage);
                if (selectedMainSkill == CarnivalMainSkill.IceNova) combat.ApplyChill(monster.model);
            }
    }

    void GalvanicConeHit(float damage, Vector2 origin)
    {
        float reach = CarnivalSkillCatalog.GalvanicConeReach * areaBonus;
        var effect = GroundSector("Galvanic cone", origin, aim, reach,
            CarnivalSkillCatalog.GalvanicConeDegrees, new Color(.35f, .75f, 1f, .55f));
        Destroy(effect.gameObject, .14f);
        damage = CarnivalMatchRules.ProjectileDamage(damage, projectileCount, piercingEnabled);
        foreach (var monster in monsters)
            if (monster.health > 0 && InSector(origin, aim, monster.position,
                reach + monster.radius, CarnivalSkillCatalog.GalvanicConeDegrees))
                DamageMonster(monster, damage);
    }

    void FireProjectiles(float speed, float radius, float life, float damage, Vector2? spawnOrigin = null)
    {
        damage = CarnivalMatchRules.ProjectileDamage(damage, projectileCount, piercingEnabled);
        bool galvanic = selectedMainSkill == CarnivalMainSkill.GalvanicArrow;
        int count = projectileCount + (galvanic ? CarnivalSkillCatalog.GalvanicBaseProjectiles - 1 : 0);
        for (int i = 0; i < count; i++)
        {
            float angle = galvanic ?
                ((float)skillRandom.NextDouble() - .5f) * CarnivalSkillCatalog.GalvanicConeDegrees * Mathf.Deg2Rad :
                (i - (count - 1) * .5f) * 9f * Mathf.Deg2Rad;
            if (!galvanic && selectedWeapon == Weapon.Bow && selectedSupports.Contains(CarnivalSupportSkill.Nimis))
                angle += ((float)skillRandom.NextDouble() * 120f - 60f) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(aim.x * Mathf.Cos(angle) - aim.y * Mathf.Sin(angle),
                aim.x * Mathf.Sin(angle) + aim.y * Mathf.Cos(angle));
            CreateShot((spawnOrigin ?? heroPosition) + direction * .55f, direction * speed, false, radius, life, damage);
        }
    }

    void MeleeHit(bool cyclone, float damage)
    {
        var directHits = new List<Monster>();
        foreach (var m in monsters)
        {
            if (m.health <= 0) continue;
            bool inRange = cyclone ?
                Vector2.Distance(heroPosition, m.position) <= 1.5f * areaBonus + m.radius :
                InSector(heroPosition, aim, m.position, 2.15f * areaBonus + m.radius, 105f);
            if (inRange) directHits.Add(m);
        }
        int repeats = selectedSupports.Contains(CarnivalSupportSkill.Multistrike) ? 2 : 1;
        foreach (var m in directHits)
        {
            for (int i = 0; i < repeats; i++) DamageMonster(m, damage * (repeats == 1 ? 1f : .65f));
            if (selectedSupports.Contains(CarnivalSupportSkill.Shockwave))
            {
                HitCircle(m.position, m.radius + .7f, damage * .35f);
                var effect = GroundSector("Blade shockwave", m.position, aim,
                    m.radius + .7f, 360f, new Color(.65f, .92f, 1f, .5f));
                Destroy(effect.gameObject, .15f);
            }
        }
        if (chainCount <= 0) return;
        var directSet = new HashSet<Monster>(directHits);
        var shocked = new HashSet<Monster>();
        foreach (var source in directHits)
        {
            var effect = GroundSector("Chain shockwave", source.position, Vector2.up,
                source.radius + .5f, 360f, new Color(.55f, .9f, 1f, .55f));
            Destroy(effect.gameObject, .17f);
            foreach (var target in monsters)
            {
                if (target.health <= 0 || directSet.Contains(target) || shocked.Contains(target)) continue;
                if (Vector2.Distance(source.position, target.position) >
                    source.radius + target.radius + .3f) continue;
                DamageMonster(target, damage * .5f);
                shocked.Add(target);
            }
        }
    }

    void DamageMonster(Monster monster, float damage)
    {
        combat.DamageMonster(monster.model, damage, maxHealth, lifesteal);
        if (selectedSupports.Contains(CarnivalSupportSkill.Bleed)) combat.ApplyBleed(monster.model);
    }

    void Shockwave()
    {
        if (!match.TrySpendCp(CarnivalMatchRules.ShockwaveCost)) return;
        foreach (var m in monsters)
        {
            Vector2 away = m.position - heroPosition;
            if (away.magnitude > 2.6f * areaBonus) continue;
            DamageMonster(m, 3f * damageBonus);
            m.position = Clamp(m.position + away.normalized * .65f, m.radius);
        }
        for (int i = shots.Count - 1; i >= 0; i--)
            if (shots[i].hostile && Vector2.Distance(shots[i].position, heroPosition) < 2.6f) DeleteShot(i);
        Announce("CP SHOCKWAVE", .8f);
    }

    void CreateShot(Vector2 pos, Vector2 velocity, bool hostile, float radius, float life, float damage)
    {
        var body = CarnivalActor3D.Projectile(hostile ? "Monster projectile" : "Hero projectile",
            pos, velocity, !hostile && selectedWeapon == Weapon.Bow, hostile, radius);
        var shot = new Shot { position = pos, velocity = velocity, hostile = hostile, radius = radius,
            life = life, damage = damage, body = body,
            piercing = !hostile && (piercingEnabled || selectedMainSkill == CarnivalMainSkill.FreezingPulse),
            chainsRemaining = hostile ? 0 : chainCount + CarnivalSkillCatalog.BaseChainCount(selectedMainSkill) };
        if (!hostile)
        {
            shot.model.SplitOnImpact = selectedMainSkill == CarnivalMainSkill.TornadoShot;
            if (shot.model.SplitOnImpact)
                shot.model.RangeRemaining = CarnivalSkillCatalog.TornadoShotRange - .55f;
            shot.model.BouncesRemaining = selectedMainSkill == CarnivalMainSkill.Spark ? 2 : 0;
            shot.model.Returning = selectedWeapon == Weapon.Bow && selectedSupports.Contains(CarnivalSupportSkill.Nimis);
        }
        shots.Add(shot);
        combat.Add(shot.model);
    }

    void UpdateMonsters(float dt)
    {
        foreach (var m in monsters)
        {
            if (m.health <= 0) continue;
            CarnivalMonsterAction previous = m.action;
            bool attack = combat.AdvanceMonster(m.model, dt, Time.time);
            if (m.warning != null)
            {
                if (previous == CarnivalMonsterAction.Moving && m.action == CarnivalMonsterAction.Windup)
                    m.warning.gameObject.SetActive(true);
                if (m.action == CarnivalMonsterAction.Strike || m.action == CarnivalMonsterAction.Recovery)
                    m.warning.gameObject.SetActive(false);
            }
            if (attack) PerformMonsterAttack(m);

            if (state != State.Playing) return;
        }
        SeparateMonsters();
        foreach (var m in monsters)
        {
            if (m.health <= 0) continue;
            m.body.position = At(m.position, 0);
            Vector2 facing = m.action == CarnivalMonsterAction.Moving ? heroPosition - m.position : m.attackDirection;
            if (facing.sqrMagnitude > .001f)
                m.body.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0, facing.y));
            if (m.displayedAction != m.action)
            {
                CarnivalActor3D.Recolor(m.body, m.action == CarnivalMonsterAction.Moving ?
                    RarityBodyColor(MonsterColor(m.kind), m.rarity) : ActionColor(m.action));
                m.displayedAction = m.action;
            }
            m.shadow.position = At(m.position, .015f);
            if (m.rarityRing != null) m.rarityRing.position = At(m.position, .02f);
            float barWidth = m.kind == CarnivalMonsterKind.Boss ? 2.2f : .9f;
            m.healthBar.position = At(m.position, m.visualHeight + .23f);
            m.healthBar.localScale = new Vector3(barWidth * Mathf.Max(0, m.health / m.maxHealth), .09f, 1);
            if (m.warning != null) m.warning.position = At(m.position, .02f);
            if (m.warning != null && m.kind == CarnivalMonsterKind.Ogre)
            {
                PlaceGroundAttack(m.warning, m.position, m.attackDirection);
                m.warning.GetComponent<MeshRenderer>().material.color = m.action == CarnivalMonsterAction.Locked ?
                    new Color(1f, .72f, .15f, .85f) : m.action == CarnivalMonsterAction.Strike ?
                    new Color(1f, .12f, .2f, .92f) : new Color(1f, .3f, .2f, .34f);
            }
        }
    }

    float AttackRange(Monster m)
    {
        return CarnivalMatchRules.AttackRange(m.kind, m.spacingRadius);
    }

    void PerformMonsterAttack(Monster m)
    {
        CarnivalMonsterAttackResult attack = combat.ResolveMonsterAttack(m.model, PlayerRadius);
        if (attack.SpawnProjectile)
            CreateShot(new Vector2(attack.ProjectileX, attack.ProjectileY),
                new Vector2(attack.VelocityX, attack.VelocityY), true,
                attack.ProjectileRadius, attack.ProjectileLife, attack.ProjectileDamage);
        if (attack.DirectDamage > 0) HurtHero(attack.DirectDamage);
    }

    bool InBeam(Vector2 origin, Vector2 direction, Vector2 target, float length, float width, float radius)
    {
        return CarnivalCombatGeometry.InBeam(origin.x, origin.y, direction.x, direction.y,
            target.x, target.y, length, width, radius);
    }

    bool InSector(Vector2 origin, Vector2 direction, Vector2 target, float reach, float degrees)
    {
        return CarnivalCombatGeometry.InSector(origin.x, origin.y, direction.x, direction.y,
            target.x, target.y, reach, degrees);
    }

    Transform GroundRectangle(string name, Vector2 origin, Vector2 direction, float length, float width, Color color)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-width * .5f, 0, 0), new Vector3(width * .5f, 0, 0),
            new Vector3(-width * .5f, 0, length), new Vector3(width * .5f, 0, length) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().material = GroundMaterial(color);
        PlaceGroundAttack(go.transform, origin, direction);
        return go.transform;
    }

    Transform GroundSector(string name, Vector2 origin, Vector2 direction, float reach, float degrees, Color color)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        const int steps = 24;
        var vertices = new Vector3[steps + 2];
        var triangles = new int[steps * 3];
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Deg2Rad * (-degrees * .5f + degrees * i / steps);
            vertices[i + 1] = new Vector3(Mathf.Sin(angle) * reach, 0, Mathf.Cos(angle) * reach);
            if (i == steps) continue;
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
        var mesh = new Mesh { vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().material = GroundMaterial(color);
        PlaceGroundAttack(go.transform, origin, direction);
        return go.transform;
    }

    Material GroundMaterial(Color color)
    {
        // A referenced Resources material keeps this shader in standalone builds.
        var source = Resources.Load<Material>("CarnivalGround");
        if (source == null)
        {
            Debug.LogError("CarnivalGround material is missing from Resources.");
            return null;
        }
        var material = new Material(source);
        material.SetFloat("_Surface", 1);
        material.SetFloat("_Blend", 0);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.color = color;
        return material;
    }

    void PlaceGroundAttack(Transform visual, Vector2 origin, Vector2 direction)
    {
        visual.position = At(origin, .05f);
        visual.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.y), Vector3.up);
    }

    Color ActionColor(CarnivalMonsterAction action)
    {
        if (action == CarnivalMonsterAction.Windup) return new Color(1f, .85f, .24f);
        if (action == CarnivalMonsterAction.Locked) return new Color(1f, .55f, .16f);
        if (action == CarnivalMonsterAction.Strike) return new Color(1f, .3f, .3f);
        if (action == CarnivalMonsterAction.Recovery) return new Color(.68f, .58f, 1f);
        return new Color(.62f, 1f, 1f);
    }

    Color MonsterColor(CarnivalMonsterKind kind)
    {
        if (kind == CarnivalMonsterKind.Skitter) return new Color(.38f, .86f, .43f);
        if (kind == CarnivalMonsterKind.Spitter) return new Color(.64f, .43f, .92f);
        if (kind == CarnivalMonsterKind.Colossus) return new Color(.43f, .62f, .78f);
        if (kind == CarnivalMonsterKind.Ogre) return new Color(.88f, .51f, .25f);
        return new Color(.82f, .25f, .29f);
    }

    Color RarityBodyColor(Color baseColor, CarnivalMonsterRarity rarity)
    {
        return rarity == CarnivalMonsterRarity.Rare ? Color.Lerp(baseColor, new Color(1f, .76f, .13f), .5f) :
            rarity == CarnivalMonsterRarity.Magic ? Color.Lerp(baseColor, new Color(.3f, .48f, 1f), .45f) : baseColor;
    }

    void SeparateMonsters()
    {
        combat.ResolveContacts(PlayerRadius, match.IsRespawning || hero == null);
        if (hero != null) hero.position = At(heroPosition, 0);
        if (heroShadow != null) heroShadow.position = At(heroPosition, .015f);
        UpdateCamera();
    }

    void UpdateShots(float dt)
    {
        for (int i = shots.Count - 1; i >= 0; i--)
        {
            var shot = shots[i];
            Vector2 previous = shot.position;
            bool spent = combat.AdvanceProjectile(shot.model, dt);
            if (spent && !shot.hostile && shot.model.BouncesRemaining > 0 && shot.life > 0)
            {
                shot.model.BouncesRemaining--;
                shot.position = previous;
                shot.velocity = -shot.velocity;
                shot.body.rotation = Quaternion.LookRotation(new Vector3(shot.velocity.x, 0, shot.velocity.y));
                spent = false;
            }
            if (spent && !shot.hostile && shot.model.Returning && !shot.model.SplitOnImpact)
            {
                shot.model.Returning = false;
                Vector2 toward = heroPosition - shot.position;
                shot.velocity = toward.sqrMagnitude > .01f ? toward.normalized * 12f : -shot.velocity;
                shot.life = .8f;
                shot.body.rotation = Quaternion.LookRotation(new Vector3(shot.velocity.x, 0, shot.velocity.y));
                spent = false;
            }
            if (!spent && shot.hostile)
            {
                if (DistanceToSegment(heroPosition, previous, shot.position, out _) < shot.radius + PlayerRadius)
                {
                    HurtHero(shot.damage);
                    spent = true;
                }
            }
            else if (!spent)
            {
                for (int contact = 0; contact < 8 && !spent; contact++)
                {
                    CarnivalMonsterState hit = combat.FirstProjectileHit(shot.model, previous.x, previous.y);
                    if (hit == null) break;
                    if (shot.model.SplitOnImpact)
                    {
                        float along;
                        DistanceToSegment(new Vector2(hit.X, hit.Y), previous, shot.position, out along);
                        shot.position = Vector2.Lerp(previous, shot.position, along);
                    }
                    shot.model.HitMonsterIds.Add(hit.Id);
                    combat.DamageMonster(hit, shot.damage, maxHealth, lifesteal);
                    if (selectedMainSkill == CarnivalMainSkill.FreezingPulse) combat.ApplyChill(hit);
                    if (shot.model.SplitOnImpact) { spent = true; break; }

                    CarnivalMonsterState bounceTarget = shot.chainsRemaining > 0 ?
                        combat.ChainTarget(shot.model, hit) : null;
                    if (bounceTarget != null)
                    {
                        shot.chainsRemaining--;
                        shot.position = new Vector2(hit.X, hit.Y);
                        shot.velocity = new Vector2(bounceTarget.X - hit.X, bounceTarget.Y - hit.Y).normalized *
                            shot.velocity.magnitude;
                        shot.body.rotation = Quaternion.LookRotation(new Vector3(shot.velocity.x, 0, shot.velocity.y));
                        break;
                    }
                    if (!shot.piercing) spent = true;
                }
            }
            if (!spent && shot.model.SplitOnImpact && shot.model.RangeRemaining <= 0) spent = true;
            if (spent && !shot.hostile && shot.model.Returning && !shot.model.SplitOnImpact)
            {
                shot.model.Returning = false;
                Vector2 toward = heroPosition - shot.position;
                shot.velocity = toward.sqrMagnitude > .01f ? toward.normalized * 12f : -shot.velocity;
                shot.life = .8f;
                shot.body.rotation = Quaternion.LookRotation(new Vector3(shot.velocity.x, 0, shot.velocity.y));
                spent = false;
            }
            if (spent && shot.model.SplitOnImpact && !shot.hostile)
                SplitTornadoShot(shot);
            if (!spent) shot.body.position = At(shot.position, .65f);
            if (spent) DeleteShot(i);
            if (state != State.Playing) return;
        }
    }

    void SplitTornadoShot(Shot primary)
    {
        float speed = primary.velocity.magnitude;
        for (int i = -1; i <= 1; i++)
        {
            float angle = (float)skillRandom.NextDouble() * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            CreateShot(primary.position + direction * .2f, direction * speed, false,
                primary.radius * .8f, .5f, primary.damage * .5f);
            shots[shots.Count - 1].model.SplitOnImpact = false;
        }
    }

    float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end, out float alongPath)
    {
        return CarnivalCombatGeometry.DistanceToSegment(point.x, point.y, start.x, start.y,
            end.x, end.y, out alongPath);
    }

    void DeleteShot(int index)
    {
        if (shots[index].body != null) Destroy(shots[index].body.gameObject);
        combat.Remove(shots[index].model);
        shots.RemoveAt(index);
    }

    void RemoveDeadMonsters()
    {
        for (int i = monsters.Count - 1; i >= 0; i--)
        {
            var m = monsters[i];
            if (m.health > 0) continue;
            match.RegisterKill(m.kind, m.rarity);
            DestroyMonsterVisuals(m);
            combat.Remove(m.model);
            monsters.RemoveAt(i);
        }
    }

    void HurtHero(float damage)
    {
        if (match.IsRespawning || invulnerable > 0) return;
        health = Mathf.Max(0, health - CarnivalMatchRules.DamageAfterGuard(damage, damageTakenBonus));
        invulnerable = .65f;
        if (health <= 0)
        {
            match.BeginRespawn(Time.realtimeSinceStartupAsDouble);
            if (hero != null) hero.gameObject.SetActive(false);
            if (heroShadow != null) heroShadow.gameObject.SetActive(false);
            if (weaponVisual != null) weaponVisual.gameObject.SetActive(false);
            if (slashVisual != null) slashVisual.gameObject.SetActive(false);
            if (cycloneVisual != null) cycloneVisual.gameObject.SetActive(false);
            slashTime = 0;
        }
    }

    void RespawnHero()
    {
        match.FinishRespawn();
        health = maxHealth;
        invulnerable = 2f;
        heroPosition = Vector2.zero;
        if (hero != null)
        {
            hero.position = At(heroPosition, 0);
            hero.gameObject.SetActive(true);
        }
        if (heroShadow != null)
        {
            heroShadow.position = At(heroPosition, .015f);
            heroShadow.gameObject.SetActive(true);
        }
        if (weaponVisual != null) weaponVisual.gameObject.SetActive(true);
        UpdateCamera();
        Announce("RESPAWNED", 1f);
    }

    void UpdateCamera()
    {
        if (viewCamera == null) return;
        Vector3 target = At(heroPosition, .75f);
        viewCamera.transform.position = target + new Vector3(-9, 12, -9);
        viewCamera.transform.LookAt(target);
    }

    void LateUpdate()
    {
        if (hudCanvas == null || weaponButtons[0] == null || versusButton == null) BuildHud();
        UpdateCamera();
        Quaternion faceCamera = Quaternion.LookRotation(-viewCamera.transform.forward, Vector3.up);
        if (weaponVisual != null)
        {
            if (cycloneVisual != null && cycloneVisual.gameObject.activeSelf)
            {
                float angle = Time.time * 12f;
                Vector2 orbit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                weaponVisual.rotation = Quaternion.LookRotation(new Vector3(-orbit.y, 0, orbit.x));
                weaponVisual.position = At(heroPosition + orbit * .85f, .58f);
            }
            else
            {
                weaponVisual.rotation = Quaternion.LookRotation(new Vector3(aim.x, 0, aim.y));
                weaponVisual.position = At(heroPosition + aim * .52f, .58f);
            }
        }
        foreach (var m in monsters)
        {
            if (m.healthBar != null) m.healthBar.rotation = faceCamera;
        }

        int secondsLeft = Mathf.CeilToInt((float)System.Math.Max(0, match.PhaseDeadline - Time.realtimeSinceStartupAsDouble));
        statusLabel.text = "HP " + Mathf.CeilToInt(health) + "/" + Mathf.CeilToInt(maxHealth) +
            "    STAGE " + match.Stage + "/5    TIME " + secondsLeft + "s\nSCORE " + match.Score +
            "    CP " + match.Cp + "    DASH " + dashes + "/" + maxDashes +
            "\n" + CarnivalSkillCatalog.MainName(selectedMainSkill) +
            "    SUPPORTS " + selectedSupports.Count;
        int respawnSecondsLeft = Mathf.CeilToInt((float)System.Math.Max(0, match.RespawnAt - Time.realtimeSinceStartupAsDouble));
        bannerLabel.text = state == State.Playing && match.IsRespawning ?
            "RESPAWN IN " + respawnSecondsLeft + "s" : state == State.Playing ? bannerTime > 0 ? banner :
            monsters.Count == 0 && match.Stage < CarnivalMatchRules.FinalStage ? "NEXT WAVE INCOMING" : "" : "";
        menuShade.SetActive(state != State.Playing);
        if (state != State.Playing)
        {
            menuTitle.text = state == State.Menu ? "MONSTER CARNIVAL" :
                state == State.VersusLobby ? "1V1 CARNIVAL" :
                state == State.WeaponChoice ? "CHOOSE A WEAPON" :
                state == State.SkillChoice ? "CHOOSE A MAIN SKILL" :
                state == State.UpgradeChoice ? "CHOOSE A SUPPORT  ·  " + secondsLeft + "s" :
                state == State.Won ? "VICTORY!" : "GAME OVER";
            menuBody.text = state == State.Menu ? "Choose how to play." :
                state == State.VersusLobby ?
                    "Send monsters to your opponent with CP.\nDedicated server connection is not ready yet." :
                state == State.WeaponChoice ? "Choose your weapon first." :
                state == State.SkillChoice ? "Choose one main skill. Effects will be added later." :
                state == State.UpgradeChoice ? pendingUpgrade >= 0 ?
                    "Support locked in. Next stage starts when the timer ends." :
                    "Support for " + CarnivalSkillCatalog.MainName(selectedMainSkill) +
                    ". Effects are not active yet." :
                "Stage " + match.Stage + "/5     Score " + match.Score;
            menuButton.gameObject.SetActive(state == State.Menu || state == State.VersusLobby ||
                state == State.Lost || state == State.Won);
            menuButton.anchoredPosition = state == State.Menu ? new Vector2(-132, -113) : new Vector2(0, -113);
            menuButtonText.text = state == State.Menu ? "SOLO PRACTICE" :
                state == State.VersusLobby ? "BACK" : "RESTART";
            versusButton.gameObject.SetActive(state == State.Menu);
            for (int i = 0; i < weaponButtons.Length; i++)
            {
                bool choosing = state == State.WeaponChoice || state == State.SkillChoice ||
                    state == State.UpgradeChoice;
                weaponButtons[i].gameObject.SetActive(choosing);
                if (!choosing) continue;
                choiceTitles[i].text = state == State.WeaponChoice ? weaponNames[i] :
                    state == State.SkillChoice ? CarnivalSkillCatalog.MainName(
                        CarnivalSkillCatalog.MainForWeapon((int)selectedWeapon, i)) :
                    CarnivalSkillCatalog.SupportName(offeredSupports[i]);
                choiceDescriptions[i].text = state == State.WeaponChoice ?
                    i == 0 ? "8–18 weapon damage" : i == 1 ? "5–13 weapon damage" :
                    "Damage varies by skill" :
                    state == State.SkillChoice ? MainSkillDamageDescription(
                        CarnivalSkillCatalog.MainForWeapon((int)selectedWeapon, i)) :
                    CarnivalSkillCatalog.IsDefault(offeredSupports[i]) ?
                        "DEFAULT SUPPORT\nEffect later" : "SUPPORT\nEffect later";
                choiceTitles[i].fontSize = state == State.UpgradeChoice ? 15 : 18;
                weaponButtonImages[i].color = state == State.UpgradeChoice && pendingUpgrade == i ?
                    new Color(.36f, .72f, .48f) : i == 0 ? new Color(.28f, .48f, .58f) :
                    i == 1 ? new Color(.32f, .43f, .58f) : new Color(.4f, .37f, .57f);
            }
        }
    }

    string MainSkillDamageDescription(CarnivalMainSkill skill)
    {
        string range = CarnivalSkillCatalog.MinDamage(skill).ToString("0") + "–" +
            CarnivalSkillCatalog.MaxDamage(skill).ToString("0");
        if (selectedWeapon == Weapon.Wand) return range + " base spell damage";
        return range + " weapon damage\n" +
            (CarnivalSkillCatalog.AttackDamageMultiplier(skill) * 100f).ToString("0") +
            "% skill damage";
    }

    RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;
        return rect;
    }

    Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Vector2 pos, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, min, max, pos, size);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    Text Label(string name, Transform parent, Vector2 min, Vector2 max, Vector2 pos, Vector2 size,
        int sizePx, TextAnchor alignment, Color color)
    {
        var rect = Rect(name, parent, min, max, pos, size);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = uiFont;
        text.fontSize = sizePx;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    void BuildHud()
    {
        if (hudCanvas != null && weaponButtons[0] != null && versusButton != null) return;
        if (hudCanvas != null) Destroy(hudCanvas.gameObject);
        uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 24);
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (EventSystem.current == null)
        {
            var events = new GameObject("Carnival UI Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var root = new GameObject("Carnival HUD", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        hudCanvas = root.GetComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 1000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(960, 540);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = .5f;

        var status = Panel("Status", root.transform, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(225, -58), new Vector2(430, 96), new Color(.025f, .04f, .065f, .94f));
        statusLabel = Label("Status text", status.transform, Vector2.zero, Vector2.one, Vector2.zero,
            new Vector2(-20, -10), 17, TextAnchor.MiddleLeft, Color.white);
        bannerLabel = Label("Stage banner", root.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -115), new Vector2(500, 50), 26, TextAnchor.MiddleCenter, new Color(1, .9f, .53f));
        Label("Controls", root.transform, new Vector2(.5f, 0), new Vector2(.5f, 0),
            new Vector2(0, 23), new Vector2(840, 36), 16, TextAnchor.MiddleCenter, Color.white).text =
            "LEFT CLICK MOVE   ·   RIGHT CLICK ATTACK   ·   SHIFT DASH   ·   E SHOCKWAVE (20 CP)";
        var actionLegend = Panel("Monster action legend", root.transform, new Vector2(.5f, 0),
            new Vector2(.5f, 0), new Vector2(0, 61), new Vector2(680, 32),
            new Color(.025f, .04f, .065f, .88f));
        Label("Action colors", actionLegend.transform, Vector2.zero, Vector2.one, Vector2.zero,
            Vector2.zero, 16, TextAnchor.MiddleCenter, Color.white).text =
            "<color=#9EFFFF>MOVE</color>   <color=#FFDA3D>PREPARE</color>   " +
            "<color=#FF5555>ATTACK</color>   <color=#AE94FF>RECOVER</color>";

        var summonPanel = Panel("Summon panel", root.transform, new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(-105, -122), new Vector2(188, 224), new Color(.025f, .04f, .065f, .94f));
        Label("Summon title", summonPanel.transform, new Vector2(.5f, 1), new Vector2(.5f, 1),
            new Vector2(0, -20), new Vector2(170, 34), 19, TextAnchor.MiddleCenter, Color.white).text = "SUMMON MONSTER";
        for (int i = 0; i < summonButtons.Length; i++)
        {
            var button = Panel("Summon " + summonNames[i], summonPanel.transform,
                new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -58 - i * 45),
                new Vector2(160, 38), i == 3 ? new Color(.8f, .47f, .31f) : new Color(.24f, .37f, .53f));
            summonButtons[i] = button.rectTransform;
            Label("Button label", button.transform, Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, 18, TextAnchor.MiddleCenter, Color.white).text = summonNames[i];
        }

        var shade = Panel("Menu shade", root.transform, Vector2.zero, Vector2.one, Vector2.zero,
            Vector2.zero, new Color(.01f, .02f, .04f, .88f));
        menuShade = shade.gameObject;
        var card = Panel("Menu card", shade.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            Vector2.zero, new Vector2(620, 310), new Color(.08f, .14f, .22f, 1));
        menuTitle = Label("Menu title", card.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(0, 94), new Vector2(570, 60), 34, TextAnchor.MiddleCenter, Color.white);
        menuBody = Label("Menu description", card.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(0, 33), new Vector2(550, 70), 19, TextAnchor.MiddleCenter, new Color(.83f, .9f, .97f));
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            var button = Panel("Choice card " + i, card.transform,
                new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2((i - 1) * 185, -41), new Vector2(174, 84), new Color(.2f, .34f, .48f));
            weaponButtons[i] = button.rectTransform;
            weaponButtonImages[i] = button;
            button.raycastTarget = true;
            int choice = i;
            var choiceButton = button.gameObject.AddComponent<Button>();
            choiceButton.transition = Selectable.Transition.None;
            choiceButton.onClick.AddListener(() => SelectChoice(choice));
            choiceTitles[i] = Label("Card title", button.transform, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), new Vector2(0, 17), new Vector2(168, 46),
                18, TextAnchor.MiddleCenter, Color.white);
            choiceDescriptions[i] = Label("Card description", button.transform, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), new Vector2(0, -25), new Vector2(165, 31),
                12, TextAnchor.MiddleCenter, new Color(.89f, .94f, 1f));
        }
        var start = Panel("Menu button", card.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
            new Vector2(0, -113), new Vector2(240, 54), new Color(.43f, .87f, .72f));
        menuButton = start.rectTransform;
        start.raycastTarget = true;
        var startButton = start.gameObject.AddComponent<Button>();
        startButton.transition = Selectable.Transition.None;
        startButton.onClick.AddListener(SelectMenuAction);
        menuButtonText = Label("Menu button text", start.transform, Vector2.zero, Vector2.one, Vector2.zero,
            Vector2.zero, 21, TextAnchor.MiddleCenter, new Color(.02f, .15f, .12f));
        var versus = Panel("1v1 entry button", card.transform, new Vector2(.5f, .5f),
            new Vector2(.5f, .5f), new Vector2(132, -113), new Vector2(240, 54),
            new Color(.34f, .51f, .79f));
        versusButton = versus.rectTransform;
        versus.raycastTarget = true;
        var versusUiButton = versus.gameObject.AddComponent<Button>();
        versusUiButton.transition = Selectable.Transition.None;
        versusUiButton.onClick.AddListener(() => { if (state == State.Menu) OpenVersusLobby(); });
        Label("1v1 entry label", versus.transform, Vector2.zero, Vector2.one, Vector2.zero,
            Vector2.zero, 21, TextAnchor.MiddleCenter, Color.white).text = "1V1 CARNIVAL";
    }
}
