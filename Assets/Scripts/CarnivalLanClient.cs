using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

// The LAN client sends input only. The dedicated server owns HP, CP, monsters and the clock.
public sealed class CarnivalLanClient : MonoBehaviour
{
    sealed class PendingAction
    {
        public int Id;
        public CarnivalLanAction Kind;
        public byte Value;
    }

    static readonly string[] MonsterNames = { "SWARM 10 CP", "RANGED 16 CP", "TANK 24 CP", "OGRE 30 CP" };
    static readonly Color[] MonsterColors = {
        new Color(.38f,.86f,.43f), new Color(.64f,.43f,.92f), new Color(.43f,.62f,.78f),
        new Color(.88f,.51f,.25f), new Color(.82f,.25f,.29f)
    };

    UdpClient socket;
    Camera viewCamera;
    Transform hero;
    readonly Dictionary<int, Transform> monsterVisuals = new Dictionary<int, Transform>();
    readonly Dictionary<int, Transform> rarityMarkers = new Dictionary<int, Transform>();
    readonly Dictionary<int, Vector2> monsterTargets = new Dictionary<int, Vector2>();
    readonly Dictionary<int, Transform> shotVisuals = new Dictionary<int, Transform>();
    readonly Dictionary<int, Vector2> shotTargets = new Dictionary<int, Vector2>();
    readonly Queue<PendingAction> actionQueue = new Queue<PendingAction>();
    CarnivalLanSnapshot snapshot;
    CarnivalLanWeapon weapon;
    int mainChoice;
    CarnivalMainSkill MainSkill => CarnivalSkillCatalog.MainForWeapon((int)weapon, mainChoice);
    Vector2 heroTarget, aim = Vector2.up;
    Vector2 moveTarget;
    bool hasMoveTarget;
    string host = "127.0.0.1", portText = "7777", message = "";
    int nextActionId = 1;
    double nextHello, nextInput, lastSnapshot, nextAttackVfx;
    GUIStyle titleStyle, labelStyle, buttonStyle, boxStyle;
    bool loggedConnection;

    public static bool HasAutoConnect
    {
        get
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg == "-carnival-connect") return true;
            return false;
        }
    }

    void Awake()
    {
        viewCamera = Camera.main;
        if (viewCamera == null)
        {
            var go = new GameObject("LAN Camera");
            go.tag = "MainCamera";
            viewCamera = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        viewCamera.orthographic = true;
        viewCamera.orthographicSize = 8.2f;
        viewCamera.clearFlags = CameraClearFlags.SolidColor;
        viewCamera.backgroundColor = new Color(.055f,.08f,.12f);
        UpdateCamera(Vector2.zero);
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-carnival-connect") { host = args[i + 1]; Connect(); break; }
    }

    void OnDestroy()
    {
        if (socket != null) socket.Close();
        if (hero != null) Destroy(hero.gameObject);
        foreach (var visual in monsterVisuals.Values) if (visual != null) Destroy(visual.gameObject);
        foreach (var marker in rarityMarkers.Values) if (marker != null) Destroy(marker.gameObject);
        foreach (var visual in shotVisuals.Values) if (visual != null) Destroy(visual.gameObject);
    }

    void Connect()
    {
        if (!int.TryParse(portText, out int port) || port < 1 || port > 65535)
        { message = "Enter a valid UDP port."; return; }
        try
        {
            if (socket != null) socket.Close();
            socket = new UdpClient(0);
            socket.Connect(host.Trim(), port);
            snapshot = null;
            hasMoveTarget = false;
            nextHello = 0;
            lastSnapshot = Time.realtimeSinceStartupAsDouble;
            message = "Connecting to " + host + ":" + port + "...";
        }
        catch (Exception error) { message = error.Message; if (socket != null) socket.Close(); socket = null; }
    }

    void QueueAction(CarnivalLanAction kind, byte value = 0)
    {
        if (actionQueue.Count < 16)
            actionQueue.Enqueue(new PendingAction { Id = nextActionId++, Kind = kind, Value = value });
    }

    void Update()
    {
        if (socket == null) return;
        double now = Time.realtimeSinceStartupAsDouble;
        for (int received = 0; socket.Available > 0 && received < 32; received++)
        {
            try
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] packet = socket.Receive(ref from);
                if (!CarnivalLanProtocol.ReadSnapshot(packet, out CarnivalLanSnapshot latest)) continue;
                snapshot = latest;
                lastSnapshot = now;
                if (!loggedConnection)
                { Debug.Log("Carnival LAN client connected as slot " + latest.Slot);
                    loggedConnection = true; }
                while (actionQueue.Count > 0 && actionQueue.Peek().Id <= latest.AckActionId)
                    actionQueue.Dequeue();
                ApplySnapshot(latest);
            }
            catch (SocketException error) { message = error.Message; break; }
        }

        if (snapshot == null || now - lastSnapshot > 2)
        {
            if (now >= nextHello)
            {
                byte[] hello = CarnivalLanProtocol.HelloPacket(weapon, MainSkill);
                try { socket.Send(hello, hello.Length); }
                catch (SocketException error) { message = error.Message; }
                nextHello = now + 1;
            }
            if (now - lastSnapshot > 3) message = "Server not responding. Check IP, port and firewall.";
            return;
        }

        GatherInput();
        if (now >= nextInput)
        {
            CarnivalLanInput input = CurrentInput();
            if (input.Attack && !snapshot.Respawning && now >= nextAttackVfx)
            {
                ShowLocalAttack();
                nextAttackVfx = now + (weapon == CarnivalLanWeapon.Bow ? .42 : weapon == CarnivalLanWeapon.Wand ? .6 : .48);
            }
            byte[] bytes = CarnivalLanProtocol.InputPacket(input);
            try { socket.Send(bytes, bytes.Length); }
            catch (SocketException error) { message = error.Message; }
            nextInput = now + .05;
        }

        float blend = 1 - Mathf.Exp(-18f * Time.deltaTime);
        if (hero != null) hero.position = Vector3.Lerp(hero.position, At(heroTarget, 0), blend);
        foreach (var pair in monsterVisuals)
            if (pair.Value != null && monsterTargets.TryGetValue(pair.Key, out Vector2 target))
                pair.Value.position = Vector3.Lerp(pair.Value.position, At(target, 0), blend);
        foreach (var pair in rarityMarkers)
            if (pair.Value != null && monsterTargets.TryGetValue(pair.Key, out Vector2 target))
                pair.Value.position = Vector3.Lerp(pair.Value.position, At(target, .025f), blend);
        foreach (var pair in shotVisuals)
            if (pair.Value != null && shotTargets.TryGetValue(pair.Key, out Vector2 target))
                pair.Value.position = Vector3.Lerp(pair.Value.position, At(target, .65f), blend);
        UpdateCamera(hero != null ? new Vector2(hero.position.x, hero.position.z) : heroTarget);
    }

    void ShowLocalAttack()
    {
        if (MainSkill == CarnivalMainSkill.GalvanicArrow)
            CarnivalSkillVisuals.ShowSector("LAN Galvanic cone", heroTarget, aim,
                CarnivalSkillCatalog.GalvanicConeReach, CarnivalSkillCatalog.GalvanicConeDegrees,
                new Color(.35f, .75f, 1f, .55f), .14f);
        else if (MainSkill == CarnivalMainSkill.IceNova)
            CarnivalSkillVisuals.ShowSector("LAN Ice nova", heroTarget, aim, 2.2f, 360f,
                new Color(.4f, .85f, 1f, .5f), .18f);
        else if (MainSkill == CarnivalMainSkill.Smite)
            CarnivalSkillVisuals.ShowSector("LAN Smite", heroTarget + aim * 1.8f, aim, 1.35f, 360f,
                new Color(.75f, .85f, 1f, .5f), .16f);
        else if (MainSkill == CarnivalMainSkill.Cyclone)
            CarnivalSkillVisuals.ShowSector("LAN Cyclone", heroTarget, aim, 1.5f, 360f,
                new Color(.35f, .86f, 1f, .42f), .2f);
    }

    void GatherInput()
    {
        if (snapshot == null || snapshot.Phase != CarnivalLanPhase.Combat)
        { hasMoveTarget = false; return; }
        var keys = Keyboard.current;
        if (keys != null)
        {
            if (keys.leftShiftKey.wasPressedThisFrame || keys.rightShiftKey.wasPressedThisFrame)
                QueueAction(CarnivalLanAction.Dash);
            if (keys.eKey.wasPressedThisFrame) QueueAction(CarnivalLanAction.Shockwave);
            if (keys.digit1Key.wasPressedThisFrame) QueueAction(CarnivalLanAction.Summon, 0);
            if (keys.digit2Key.wasPressedThisFrame) QueueAction(CarnivalLanAction.Summon, 1);
            if (keys.digit3Key.wasPressedThisFrame) QueueAction(CarnivalLanAction.Summon, 2);
            if (keys.digit4Key.wasPressedThisFrame) QueueAction(CarnivalLanAction.Summon, 3);
        }
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed && !PointerOverGui(mouse.position.ReadValue()))
        {
            Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
            {
                Vector3 hit = ray.GetPoint(distance);
                moveTarget = new Vector2(hit.x, hit.z);
                hasMoveTarget = true;
            }
        }
    }

    CarnivalLanInput CurrentInput()
    {
        var input = new CarnivalLanInput { AimX = aim.x, AimY = aim.y };
        if (snapshot != null && snapshot.Phase == CarnivalLanPhase.Combat)
        {
            if (hasMoveTarget)
            {
                Vector2 toward = moveTarget - heroTarget;
                float distance = toward.magnitude;
                if (distance <= .12f) hasMoveTarget = false;
                else
                {
                    Vector2 move = toward / distance * Mathf.Min(1f, distance / .5f);
                    input.MoveX = move.x; input.MoveY = move.y;
                }
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    Vector2 direction = new Vector2(hit.x, hit.z) - heroTarget;
                    if (direction.sqrMagnitude > .02f) aim = direction.normalized;
                }
                input.Attack = mouse.rightButton.isPressed && !PointerOverGui(mouse.position.ReadValue());
            }
            input.AimX = aim.x; input.AimY = aim.y;
        }
        if (actionQueue.Count > 0)
        { var pending = actionQueue.Peek(); input.ActionId = pending.Id;
            input.Action = pending.Kind; input.ActionValue = pending.Value; }
        return input;
    }

    bool PointerOverGui(Vector2 screen)
    {
        float x = screen.x * 960f / Screen.width;
        float y = (Screen.height - screen.y) * 540f / Screen.height;
        return x > 740 && y < 360 || snapshot != null && snapshot.Phase != CarnivalLanPhase.Combat;
    }

    void ApplySnapshot(CarnivalLanSnapshot latest)
    {
        heroTarget = new Vector2(latest.X, latest.Y);
        if (hero == null)
            hero = CarnivalActor3D.Character("LAN Hero", true, heroTarget, 1.15f, .55f,
                new Color(.54f,.88f,.86f));
        hero.gameObject.SetActive(!latest.Respawning);
        var liveMonsters = new HashSet<int>();
        foreach (var m in latest.Monsters)
        {
            liveMonsters.Add(m.Id);
            Vector2 target = new Vector2(m.X, m.Y);
            monsterTargets[m.Id] = target;
            if (!monsterVisuals.TryGetValue(m.Id, out Transform body))
            {
                float size = m.Kind == 4 ? 2.1f : m.Kind == 3 ? 1.75f : m.Kind == 2 ? 1.45f : m.Kind == 1 ? 1f : .75f;
                float radius = CarnivalMatchRules.MonsterRadius((CarnivalMonsterKind)m.Kind);
                body = CarnivalActor3D.Character("LAN " + (CarnivalMonsterKind)m.Kind, false,
                    target, size, radius, RarityColor(MonsterColors[Math.Min(m.Kind, (byte)4)], m.Rarity));
                monsterVisuals[m.Id] = body;
                if (m.Rarity != (byte)CarnivalMonsterRarity.Normal)
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    marker.name = (CarnivalMonsterRarity)m.Rarity + " monster marker";
                    marker.transform.position = At(target, .025f);
                    marker.transform.localScale = new Vector3(radius * 2.7f, .012f, radius * 2.7f);
                    marker.GetComponent<Renderer>().material.color = m.Rarity == (byte)CarnivalMonsterRarity.Rare ?
                        new Color(1f, .74f, .12f) : new Color(.25f, .45f, 1f);
                    Destroy(marker.GetComponent<Collider>());
                    rarityMarkers[m.Id] = marker.transform;
                }
            }
            Color color = m.Action == (byte)CarnivalMonsterAction.Windup ? new Color(1f,.85f,.24f) :
                m.Action == (byte)CarnivalMonsterAction.Strike ? new Color(1f,.3f,.3f) :
                m.Action == (byte)CarnivalMonsterAction.Recovery ? new Color(.68f,.58f,1f) :
                RarityColor(MonsterColors[Math.Min(m.Kind, (byte)4)], m.Rarity);
            CarnivalActor3D.Recolor(body, color);
        }
        foreach (int id in new List<int>(monsterVisuals.Keys))
            if (!liveMonsters.Contains(id))
            { Destroy(monsterVisuals[id].gameObject); monsterVisuals.Remove(id); monsterTargets.Remove(id);
                if (rarityMarkers.TryGetValue(id, out Transform marker)) Destroy(marker.gameObject);
                rarityMarkers.Remove(id); }

        var liveShots = new HashSet<int>();
        foreach (var shot in latest.Shots)
        {
            liveShots.Add(shot.Id);
            Vector2 target = new Vector2(shot.X, shot.Y);
            shotTargets[shot.Id] = target;
            if (shotVisuals.ContainsKey(shot.Id)) continue;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = shot.Hostile ? "LAN monster shot" : "LAN hero shot";
            go.transform.position = At(target, .65f);
            go.transform.localScale = Vector3.one * Math.Max(.28f, shot.Radius * 2);
            go.GetComponent<Renderer>().material.color = shot.Hostile ? new Color(1f,.34f,.25f) :
                new Color(.47f,.85f,1f);
            Destroy(go.GetComponent<Collider>());
            shotVisuals[shot.Id] = go.transform;
        }
        foreach (int id in new List<int>(shotVisuals.Keys))
            if (!liveShots.Contains(id))
            { Destroy(shotVisuals[id].gameObject); shotVisuals.Remove(id); shotTargets.Remove(id); }
    }

    static Vector3 At(Vector2 position, float height) => new Vector3(position.x, height, position.y);

    static Color RarityColor(Color baseColor, byte rarity) =>
        rarity == (byte)CarnivalMonsterRarity.Rare ? Color.Lerp(baseColor, new Color(1f, .76f, .13f), .5f) :
        rarity == (byte)CarnivalMonsterRarity.Magic ? Color.Lerp(baseColor, new Color(.3f, .48f, 1f), .45f) : baseColor;

    void UpdateCamera(Vector2 position)
    {
        Vector3 target = At(position, .75f);
        viewCamera.transform.position = target + new Vector3(-9, 12, -9);
        viewCamera.transform.LookAt(target);
    }

    void PrepareStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 29, fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, wordWrap = true,
            normal = { textColor = Color.white } };
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 19, fontStyle = FontStyle.Bold };
        boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 19, normal = { textColor = Color.white } };
    }

    void OnGUI()
    {
        PrepareStyles();
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 960f, Screen.height / 540f, 1));
        if (socket == null)
        {
            GUI.Box(new Rect(260,25,440,500), "", boxStyle);
            GUI.Label(new Rect(285,40,390,45), "1V1 LAN CARNIVAL", titleStyle);
            GUI.Label(new Rect(300,95,360,30), "Server IP address", labelStyle);
            host = GUI.TextField(new Rect(300,125,360,40), host, 64);
            GUI.Label(new Rect(300,175,130,30), "UDP port", labelStyle);
            portText = GUI.TextField(new Rect(430,170,130,40), portText, 5);
            GUI.Label(new Rect(300,220,360,30), "Weapon", labelStyle);
            var nextWeapon = (CarnivalLanWeapon)GUI.Toolbar(new Rect(300,250,360,42), (int)weapon,
                new[] { "SWORD", "BOW", "WAND" });
            if (nextWeapon != weapon) { weapon = nextWeapon; mainChoice = 0; }
            GUI.Label(new Rect(300,305,360,30), "Main skill (effects later)", labelStyle);
            mainChoice = GUI.Toolbar(new Rect(300,337,360,48), mainChoice, new[] {
                CarnivalSkillCatalog.MainName(CarnivalSkillCatalog.MainForWeapon((int)weapon, 0)),
                CarnivalSkillCatalog.MainName(CarnivalSkillCatalog.MainForWeapon((int)weapon, 1)),
                CarnivalSkillCatalog.MainName(CarnivalSkillCatalog.MainForWeapon((int)weapon, 2)) });
            if (GUI.Button(new Rect(345,405,270,48), "CONNECT", buttonStyle)) Connect();
            GUI.Label(new Rect(275,465,410,45), message, labelStyle);
            return;
        }
        if (snapshot == null)
        {
            GUI.Box(new Rect(280,170,400,175), "", boxStyle);
            GUI.Label(new Rect(300,190,360,45), "CONNECTING TO SERVER", titleStyle);
            GUI.Label(new Rect(310,250,350,75), message, labelStyle);
            return;
        }
        string stage = snapshot.Phase == CarnivalLanPhase.Waiting ? "WAITING FOR PLAYER 2" :
            snapshot.Phase == CarnivalLanPhase.Finished ? "MATCH FINISHED" :
            "STAGE " + snapshot.Stage + "/5  " + snapshot.Phase.ToString().ToUpper() +
            "  " + Mathf.CeilToInt(snapshot.SecondsLeft) + "s";
        GUI.Box(new Rect(12,10,525,130), "", boxStyle);
        GUI.Label(new Rect(24,18,500,34), stage, labelStyle);
        GUI.Label(new Rect(24,52,500,80), "HP " + Mathf.CeilToInt(snapshot.Health) + "/" +
            Mathf.CeilToInt(snapshot.MaxHealth) + "    CP " + snapshot.Cp + "    SCORE " + snapshot.Score +
            "\n" + CarnivalSkillCatalog.MainName(MainSkill) +
            "\nOPPONENT  HP " + Mathf.CeilToInt(snapshot.EnemyHealth) +
            "    SCORE " + snapshot.EnemyScore, labelStyle);

        if (snapshot.Phase == CarnivalLanPhase.Combat)
        {
            GUI.Box(new Rect(745,10,200,330), "", boxStyle);
            GUI.Label(new Rect(755,20,185,38), "SEND MONSTER", labelStyle);
            for (byte i = 0; i < 4; i++)
                if (GUI.Button(new Rect(758,65+i*62,175,52), MonsterNames[i], buttonStyle))
                    QueueAction(CarnivalLanAction.Summon, i);
            GUI.Label(new Rect(170,495,650,35),
                "LEFT CLICK MOVE  ·  RIGHT CLICK ATTACK  ·  SHIFT DASH  ·  1-4 SUMMON  ·  E SHOCKWAVE", labelStyle);
            if (snapshot.Respawning)
                GUI.Label(new Rect(300,230,360,70), "RESPAWNING...", titleStyle);
        }
        else if (snapshot.Phase == CarnivalLanPhase.Draft)
        {
            GUI.Box(new Rect(185,170,590,205), "", boxStyle);
            GUI.Label(new Rect(210,184,540,42), "CHOOSE A SUPPORT", titleStyle);
            for (byte i = 0; i < 3; i++)
            {
                byte card = snapshot.Cards[i];
                string name = card < System.Enum.GetValues(typeof(CarnivalSupportSkill)).Length ?
                    CarnivalSkillCatalog.SupportName((CarnivalSupportSkill)card) : "CARD";
                if (GUI.Button(new Rect(210+i*185,245,170,95), name, buttonStyle))
                    QueueAction(CarnivalLanAction.ChooseCard, i);
            }
        }
        else if (snapshot.Phase == CarnivalLanPhase.Finished)
        {
            GUI.Box(new Rect(285,170,390,180), "", boxStyle);
            string outcome = snapshot.Score == snapshot.EnemyScore ? "DRAW" :
                snapshot.Score > snapshot.EnemyScore ? "VICTORY" : "DEFEAT";
            GUI.Label(new Rect(300,200,360,70), outcome, titleStyle);
            GUI.Label(new Rect(315,285,340,45), "Final score " + snapshot.Score + " : " + snapshot.EnemyScore, labelStyle);
        }
    }
}
