# Carnival LAN test

The `Builds/LanClient` folder contains both the client and a headless LAN server. Copy the **whole folder**, including `CarnivalClient_Data`, when moving it to another PC.

## Start a match

1. On the server PC, run `Start LAN Server.bat` inside `Builds/LanClient`. Keep its window open. Allow the app through Windows Firewall on a **Private** network if prompted. The server uses UDP port **7777**.
2. On the same PC, run `CarnivalClient.exe`, select **1V1 CARNIVAL**, enter `127.0.0.1`, choose a weapon, and connect.
3. On the second PC, run `CarnivalClient.exe` from a copy of the whole folder. Select **1V1 CARNIVAL**, enter the server PC's LAN IPv4 address (for example, `192.168.0.23`), choose a weapon, and connect. Find the server PC's address with `ipconfig`.
4. Both players receive three cards at the start and after each stage. The server advances the game when the five-second choice timer ends. Both clients then play a 30-second combat stage. Stage 5 contains a boss; the final score decides the winner.

Controls: **WASD** move, **mouse** aim and attack, **Shift** dash, **1–4** or the right panel to spend CP and send monsters to the opponent, **E** shockwave (20 CP). CP summon costs: Swarm 10, Ranged 16, Tank 24, Ogre 30.

The original **Solo Practice** entry remains available. For LAN use, both PCs should be on the same local network and use the same game build. Guest Wi-Fi networks may isolate devices. No router port forwarding is needed for a local network.

The headless server currently uses the regular Windows player build with `-batchmode -nographics` because Windows Dedicated Server build support is not installed in this Unity Editor. Installing that Unity Hub module would allow a smaller dedicated server build later. The server remains a separate authoritative process in this LAN build.
