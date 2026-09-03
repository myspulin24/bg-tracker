# Hearthstone Battlegrounds Tracker – technická dokumentace

## 1. Účel a současný stav

Projekt je pilotní lokální tracker pro Hearthstone Battlegrounds. Jeho hlavní podobou je
desktopový WPF overlay pro Windows, který čte textový soubor `Power.log`, průběžně z něj
rekonstruuje stav zápasu a zobrazuje informace nad hrou. Součástí řešení je také starší
konzolová varianta, sdílená knihovna s parserem a stavovým modelem a automatické testy.

Tracker je pouze pasivní čtečka logu:

- nečte paměť procesu Hearthstone;
- neinjektuje kód do hry;
- neposílá do hry vstupy a nic neautomatizuje;
- neupravuje ani nezkracuje Blizzard `Power.log`;
- všechna vlastní data ukládá pouze lokálně na počítači uživatele.

Aktuálně z logu rekonstruuje:

- začátek a konec hry, Battlegrounds kolo a fázi;
- celé osmičlenné lobby: hrdinu, BattleTag, HP, armor, Tavern Tier, počet triplů,
  pořadí v žebříčku a vyřazení;
- vlastní desku, desku aktuálního soupeře, nabídku Boba a karty v ruce, včetně
  útoku, života, tieru, zlaté verze a klíčových slov;
- poslední známou desku každého hráče lobby, dostupnou po najetí myší na jeho řádek;
- typy minionů, které se objevily v nabídce Boba;
- zlato, cenu upgradu tavernu a slot dalšího soupeře;
- historii soubojů s výsledkem a utrpěným poškozením;
- konečné umístění.

MMR je připravené v datovém modelu, ale v pozorovaném `Power.log` pro něj neexistuje
zdroj. V UI se proto nezobrazuje vůbec; místo něj je sloupec s počtem triplů.

Stav dokumentace odpovídá implementaci k 1. září 2026.

## 2. Technologie a požadavky

- C# a .NET 8;
- WPF (`net8.0-windows`) pro desktopový overlay;
- konzolová aplikace na `net8.0`;
- xUnit pro testy;
- Windows, protože hlavní UI je WPF a automatická detekce hledá instalaci
  Hearthstonu ve windowsových umístěních;
- .NET 8 SDK nebo novější pro sestavení ze zdrojového kódu;
- Visual Studio 2022 s workloadem `.NET desktop development` je volitelné.

Spuštění ani běžné čtení logu nevyžaduje PowerShell nebo aplikaci spuštěnou jako
správce. Pokud by byly přístupové údaje konkrétní instalace Hearthstonu nestandardně
omezené, lze log vybrat ručně.

Globální nastavení projektu v `Directory.Build.props` zapíná nullable reference types,
implicitní `using` a převádí všechna compiler warnings na chyby. Release build tedy musí
projít bez varování.

## 3. Struktura řešení

| Projekt / cesta | Odpovědnost |
| --- | --- |
| `src/Tracker.Core` | Parser `Power.log`, hledání instalace hry, geometrie okna, redukce událostí do stavu, model lobby, objevování logu, tail reader, archivace zápasů, `MatchRecorder` a stahování kreseb a popisů karet. |
| `src/Tracker.Desktop` | Hlavní WPF overlay, režimy naslouchání/demo/live, view model, styly, ovládání okna a okno s patch notes ve vestavěném prohlížeči. |
| `src/Tracker.App` | Původní konzolová varianta, replay a textový dashboard. |
| `tests/Tracker.Tests` | Jednotkové testy parseru, redukce lobby a obnovy zápasového archivu. |
| `assets/bg-tracker.ico` | Ikona „BG“ pro `.exe` obou aplikací i pro okno overlaye v hlavním panelu. Obsahuje velikosti 16 až 256. |
| `.github/workflows` | CI při každém pushi a vydání nové verze při tagu `v*`. |
| `CHANGELOG.md` | Záznam změn podle Keep a Changelog; sekce vydané verze je zároveň popisem vydání na GitHubu. |
| `scripts/verify-version.ps1` | Kontrola Semantic Versioning 2.0.0: tag, `VersionPrefix` a sekce v changelogu musí sedět. Umí vypsat popis vydání z changelogu. |
| `scripts/publish-gui.ps1` | Self-contained single-file publikace WPF aplikace. |
| `scripts/publish.ps1` | Self-contained single-file publikace konzolové aplikace. |
| `artifacts` | Výstupy publikace; vznikají až při publish a nejsou součástí zdrojové architektury. |

Solution `HearthstoneBattlegroundsTracker.sln` obsahuje čtyři projekty:
`Tracker.Core`, `Tracker.Desktop`, `Tracker.App` a `Tracker.Tests`.

## 4. Hlavní tok dat

Živý desktopový režim používá tento tok:

```text
Power.log
  -> PowerLogTailReader (čtení od uložené byte pozice)
  -> PowerLogParser (jeden řádek -> PowerLogEvent)
  -> GameStateTracker (PowerLogEvent -> změna TrackerState)
  -> MainViewModel (TrackerState -> texty a kolekce pro UI)
  -> MainWindow.xaml (overlay)

Současně:
Power.log řádky probíhající hry
  -> MatchLogArchive
  -> %LOCALAPPDATA%\BattlegroundsTracker\matches\match-*.power.log
```

`GameStateTracker` drží kromě lobby i stav všech entit hry (`TrackedEntity`), ze kterého
se dopočítávají desky, nabídka Boba a ruka. Jedna třináctikolová hra vytvoří přes patnáct
tisíc entit, proto se projekce filtrují podle epochy popsané v kapitole 9.8.

Parser je stavový. Pamatuje si naposledy vytvářenou nebo aktualizovanou entitu, aby mohl
správně přiřadit následující samostatné řádky `tag=... value=...`. Při změně zdroje,
restartu live režimu, spuštění dema nebo návratu do naslouchání se vytváří nový parser i
nový `GameStateTracker`, aby se metadata z různých relací nesmíchala.

## 5. Vyhledání a aktivace Power.log

### 5.1 Zapnutí logování ve hře

Pokud Hearthstone `Power.log` nevytváří, musí být v souboru
`%LOCALAPPDATA%\Blizzard\Hearthstone\log.config` aktivní sekce:

```ini
[Power]
LogLevel=1
FilePrinting=true
ConsolePrinting=false
ScreenPrinting=false
Verbose=true
```

Hearthstone je po změně vhodné ukončit a znovu spustit. Před úpravou existujícího
`log.config` je vhodné vytvořit zálohu a zachovat ostatní sekce.

### 5.1b Okno musí zůstat v dosahu

Overlay se dá přetáhnout jen za hlavičku, takže když hlavička skončí mimo monitory, uživatel
s oknem nemůže nic dělat. Stává se to po odpojení druhého monitoru, po změně rozlišení
a hlavně při startu: `WindowStartupLocation="CenterScreen"` vystředí okno na monitor
s kurzorem, ale velikost se počítá z `SystemParameters.WorkArea`, tedy z **hlavní** pracovní
plochy. Na menším monitoru je pak okno vyšší než obrazovka a vystředění pošle horní okraj do
minusu.

Geometrii řeší `WindowPlacement.Clamp` v `Tracker.Core`, aby se dala testovat bez WPF: dostane
obdélník okna a plochu všech monitorů a vrátí polohu, ve které je hlavička vidět. Vodorovně
smí okno vyčnívat, ale musí z něj zbýt aspoň 120 bodů; svisle nesmí být nad horní hranou ani
níž, než aby zbylo 64 bodů na hlavičku. Vodorovná poloha se jinak nemění, takže okno neuteče
z monitoru, na kterém ho uživatel má.

`EnsureOnScreen` v `MainWindow` se volá na třech místech: v `Loaded`, na konci
`UpdateWindowHeight` a z `SystemEvents.DisplaySettingsChanged`. Poslední záchranou je položka
menu **Vrátit okno na obrazovku**, která okno vystředí na hlavní monitor v návrhové velikosti.

### 5.2 Automatické hledání

Desktop používá `PowerLogDiscovery`. Instalace se hledají v tomto pořadí:

- **cesta z registry**, hodnota `InstallLocation` v klíči
  `SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone` a
  `SOFTWARE\Blizzard Entertainment\Hearthstone`, ve 32bitovém i 64bitovém pohledu a v HKLM
  i HKCU. Tohle je jediný způsob, jak najít hru ve složce, na kterou by se nedalo uhodnout —
  instalátor Blizzardu nechá uživatele vybrat libovolnou cestu a lidé hru běžně stěhují na
  druhý disk;
- `%ProgramFiles(x86)%\Hearthstone`, `%ProgramFiles%\Hearthstone`
  a `%LOCALAPPDATA%\Blizzard\Hearthstone`;
- na **každém pevném disku** devět běžných umístění: `Hearthstone`, `Games\Hearthstone`,
  `Program Files\Hearthstone`, `Program Files (x86)\Hearthstone`, `Battle.net\Hearthstone`,
  `Blizzard\Hearthstone`, `Blizzard Entertainment\Hearthstone`,
  `Games\Blizzard\Hearthstone` a `Games\Battle.net\Hearthstone`.

V každé instalaci se zkouší jak přímý `Logs\Power.log`, tak session adresáře
`Logs\Hearthstone_*\Power.log`. Z existujících kandidátů se zvolí naposledy změněný soubor.
Přístupovou chybu nebo souběžnou rotaci adresáře discovery bezpečně ignoruje a ručně zadaná
cesta má přednost.

Čtení registry je za `OperatingSystem.IsWindows()`, protože `Tracker.Core` cílí na `net8.0`
a bez té podmínky by `TreatWarningsAsErrors` shodil build na CA1416. Hledání v kořenech je
oddělené v `FindInRoots`, aby se dalo testovat nad dočasnými adresáři bez skutečné instalace.

Hledá se výhradně jméno `Power.log`. Po ukončení hry Hearthstone soubor přejmenuje na
`Power_old.log`, takže mimo běžící hru discovery záměrně nenajde nic. Starší session log
lze prohlédnout přes **Vybrat log**, nebo bezpečněji konzolovým `--replay`, který nic
nezapisuje.

Když se log nenajde, tooltip u stavu vypíše, co se ověřilo: jestli běží hra, jestli má
`log.config` sekci `[Power]` a ve kterých instalacích se našel adresář `Logs`. Bez toho
vypadá chybějící logování, vypnutá hra i instalace mimo prohledávané cesty stejně.

Desktop navíc považuje automaticky nalezený log za aktuální pouze tehdy, když běží
proces `Hearthstone` a čas posledního zápisu logu není starší než přibližně jednu minutu
před startem nejstaršího nalezeného procesu hry. Tím se omezuje automatické načítání
starých session logů.

### 5.3 Režimy desktopové aplikace

Desktop má tři interní režimy:

1. **Listening / NASLOUCHÁM** – prázdný stav, jednou za sekundu hledá aktuální
   `Power.log`. Tlačítko pauzy není aktivní.
2. **Live / ŽIVĚ** – přehraje případný rozehraný vlastní archiv, dočte nové řádky
   zdrojového logu a potom jej kontroluje každých 350 ms. Přibližně jednou za pět sekund
   navíc ověří, jestli hra nezaložila nový session log, a v tom případě se na něj přepne.
   Ručně vybraný zdroj se tímhle nikdy nepřebíjí.
3. **Demo** – postupně přehrává syntetické řádky z `DemoMatch` po 180 ms. Demo je
   generované ve stejném tvaru, jaký píše hra, takže naplní lobby, desku, nabídku Boba
   i souboj.

Při startu má přednost explicitní argument `--log`, potom aktuální automaticky nalezený
log a nakonec režim naslouchání. V současné implementaci má automaticky zjištěná živá
hra přednost i během timeru dema. Je-li Hearthstone aktivní, může proto demo přejít zpět
do live režimu. Toto je známé chování vhodné k pozdější úpravě, pokud má uživatelsky
spuštěné demo zůstat izolované.

## 6. Čtení rostoucího logu

`PowerLogTailReader` otevírá log s `FileShare.ReadWrite | FileShare.Delete`, takže jej
může číst během zápisu hrou i během rotace. Pamatuje si byte pozici, seekne na ni a
zpracuje pouze nové řádky. Po dočtení zveřejní aktuální pozici přes `Position`.

Pokud je soubor kratší než uložená pozice, čtečka předpokládá zkrácení nebo nahrazení
souboru a začne znovu od nuly. Konstruktor přijímá počáteční pozici, což je základ obnovy
z checkpointu.

Callback vrací `bool`; `true` znamená uživatelsky viditelnou změnu stavu. UI se překreslí
jen tehdy, pokud alespoň jeden nový řádek takovou změnu vyvolal. Samotná archivace a
posun checkpointu probíhají i u řádků, které parser nerozpoznal.

## 7. Parser Power.log

`PowerLogParser` převádí textový řádek na `PowerLogEvent`. Událost uchovává původní
řádek a podle typu také entitu, tag, hodnotu, entity ID, player ID, Card ID, controller,
zone a příznak lokálního hráče.

Podporované druhy událostí:

- `GameCreated` – řádek obsahující `CREATE_GAME`;
- `PlayerDeclared` – vazba `EntityID`, `PlayerID` a `GameAccountId`;
- `PlayerNamed` – vazba `PlayerID` na jméno/BattleTag;
- `TagChanged` – `TAG_CHANGE Entity=... tag=... value=...` a odsazené tagy entity;
- `EntityCreated` – `FULL_ENTITY - Creating` a `FULL_ENTITY - Updating`;
- `EntityShown` – `SHOW_ENTITY - Updating Entity=...`;
- `EntityObserved` – entity descriptor na jinak neznámém řádku;
- `Unknown` – vše ostatní.

`EntityObserved` je záchranná síť pro řádky typu `GameState.DebugPrintOptions()`,
`META_DATA` nebo `BLOCK_START`. Karty v nabídce Boba vznikají přes `FULL_ENTITY - Creating`,
který nese jen Card ID; jejich jméno se poprvé objeví právě až ve výpisu options. Bez této
větve by nabídka zůstala bezejmenná.

Každá událost navíc nese příznak `IsDeferred`. Řádky ze zdrojů `PowerTaskList.` a
`PowerProcessor.` jsou opožděným přehráním animační fronty, ne aktuálním stavem hry –
podrobnosti v kapitole 9.6.

Parser normalizuje entity descriptor například z:

```text
[entityName=Alice id=2 zone=PLAY]
```

na tvar `Alice (#2)`. Současně z descriptoru zachová číselné ID, hodnotu `player` jako
controller a `zone`. Numerická entita zůstává numerická, dokud není později odhaleno
její jméno.

U `FULL_ENTITY - Creating` si parser uloží ID do `pendingEntityId`. Následující řádky
obsahující jen `tag=... value=...` pak správně připojí ke stejné entitě. Pending ID se
ruší při přechodu na nesouvisející konstrukci nebo neznámý řádek, aby tagy nepřetekly
na jinou entitu.

Formát `Power.log` není veřejné stabilní API. Regulární výrazy jsou založené na
pozorovaných formátech a po aktualizaci Hearthstonu mohou vyžadovat úpravu.

## 8. Redukce událostí do stavu hry

`GameStateTracker` drží dlouhodobé mapy identit a aplikuje každou parsovanou událost do
`TrackerState`.

### 8.1 Životní cyklus hry

- `CREATE_GAME`, když není hra aktivní, vyčistí metadata a zavolá `BeginGame`.
- `CREATE_GAME` během hry, která se ještě nedostala do prvního kola, je ignorován.
  V logu se může objevit pomocná game konstrukce a bez této ochrany by se lobby vymazala.
- `CREATE_GAME` během hry, která už kolo měla, zakládá **novou hru**. Bez toho by po pádu
  Hearthstonu, kdy `FINAL_GAMEOVER` nikdy nedorazí, zůstal tracker navždy viset
  v předchozím zápase.
- `CREATE_GAME` z opožděné animační fronty se ignoruje úplně. Právě to bývalo tou
  „duplicitní game konstrukcí“; filtr popsaný v kapitole 9.6 ho odstraní dřív.
- `STEP=FINAL_GAMEOVER` nastaví `IsGameActive=false` a uzavře vlastní zápasový archiv.
- `FINAL_WRAPUP` je pouze přechodová fáze; definitivním koncem je až
  `FINAL_GAMEOVER`.

Při nové hře se vynuluje kolo, výsledek, lobby, pozorované entity a poslední události.
Počet zaznamenaných her (`GamesSeen`) zůstává kumulativní v rámci jedné instance
trackeru.

### 8.2 Kolo a fáze

Tag `TURN` nastavuje číslo kola. Tag `STEP` se překládá přibližně takto:

| Hodnota logu | Text v UI |
| --- | --- |
| `MAIN_READY` | příprava |
| `MAIN_START_TRIGGERS` | spouštění kola |
| `MAIN_START` | začátek kola |
| `MAIN_ACTION` | nákup |
| `MAIN_COMBAT` | souboj |
| `MAIN_END` | konec kola |
| `MAIN_CLEANUP` | úklid kola |
| `MAIN_NEXT` | přechod do dalšího kola |
| `FINAL_WRAPUP` | uzavírání hry |
| `FINAL_GAMEOVER` | konec hry |

Neznámá hodnota se zobrazí malými písmeny, takže nová fáze nezmizí úplně ani před
doplněním explicitního překladu.

### 8.3 Stav entit

Každá entita se skládá do `TrackedEntity`. Zapisují se tyto tagy:

| Tag | Význam |
| --- | --- |
| `CONTROLLER` | strana, které entita patří (lokální hráč nebo sdílený soupeř) |
| `CARDTYPE`, `CARDRACE` | typ karty a rasa minionu |
| `ZONE`, `ZONE_POSITION` | zóna a pozice na desce, v nabídce nebo v ruce |
| `ATK`, `HEALTH`, `DAMAGE`, `ARMOR` | statistiky |
| `COST` | cena, mimo jiné u tlačítka upgradu tavernu |
| `TECH_LEVEL` | tier minionu |
| `PLAYER_TECH_LEVEL` | Tavern Tier hráče |
| `PLAYER_ID` | slot 1 až 8 v lobby |
| `PLAYER_LEADERBOARD_PLACE` | pořadí v žebříčku |
| `PLAYER_TRIPLES` | počet dosažených triplů |
| `PREMIUM` | zlatá karta |
| `TAUNT`, `DIVINE_SHIELD`, `REBORN`, `POISONOUS`, `VENOMOUS`, `WINDFURY` | klíčová slova |
| `BACON_COMBAT_PHASE_HERO` | dočasná kopie hrdiny pro souboj |
| `NEXT_OPPONENT_PLAYER_ID` | slot dalšího soupeře |
| `PLAYSTATE` | zejména `WON`, `LOST`, `TIED` |

Zobrazované HP je `HEALTH - DAMAGE`. Armor se zobrazuje samostatně a do efektivního HP
se nepřičítá; do zbývajících životů pro určení pořadí a vyřazení už ano. Kladné filtry
u health a tier zabraňují přepsání užitečné hodnoty některými nulovými inicializačními tagy.

Na entitě lokálního hráče se navíc čte `RESOURCES`, `TEMP_RESOURCES`, `RESOURCES_USED`
a `MAXRESOURCES` pro zlato a `BACON_WON_LAST_COMBAT` s `DAMAGE_DEALT_TO_HERO_LAST_TURN`
pro výsledek posledního souboje. Na `GameEntity` se čte `TURN`, `STEP`
a `BACON_IN_COMBAT_PHASE`.

`ObservedParticipant` je obecný historický model entit. `LobbyParticipant` je přesnější
model určený pro osm Battlegrounds hráčů. `BoardMinion` je snímek jedné karty pro UI.

### 8.4 Kolo, deska, nabídka a ruka

Tag `TURN` počítá interní tahy a na jedno Battlegrounds kolo připadají dva. `TrackerState.Round`
proto vrací `(TURN + 1) / 2`.

Projekce se počítají z entit podle controlleru a zóny:

- `PlayerBoard` – minioni lokálního hráče v zóně `PLAY` s pozicí větší než nula;
- `OpponentBoard` – totéž pro soupeřovu stranu, ale jen během fáze souboje;
- `Shop` – tytéž entity mimo souboj, tedy nabídka Boba;
- `Hand` – karty lokálního hráče v zóně `HAND`, včetně tavern kouzel.

Nabídka Boba i soupeřova deska sdílejí stejný `CONTROLLER`. Rozlišuje je jen tag
`BACON_IN_COMBAT_PHASE` na `GameEntity`.

### 8.5 Desky ostatních hráčů

Cizí desku log ukáže jen během souboje proti ní. Tracker si ji proto uloží do
`LobbyParticipant.LastBoard` spolu s číslem kola. Okamžik zachycení určuje první tag
`PROPOSED_ATTACKER` daného souboje: v tu chvíli jsou obě desky postavené a ještě nikdo
nezemřel, takže jde přesně o sestavu, se kterou soupeř nastoupil. Souboj, ve kterém
nikdo nezaútočil, se zachytí až při jeho ukončení.

Komu deska patří, se pozná podle soubojové kopie soupeřova hrdiny. Ta je spolehlivější
než `NEXT_OPPONENT_PLAYER_ID`, který se v logu mění dřív, než souboj doopravdy začne.

Jména soupeřových minionů dorazí z opožděné fronty až po zachycení. Snímek se proto
dodatečně dopisuje, jakmile se jméno entity poprvé zjistí, aby v přehledu nezůstalo
`entity #id`.

### 8.6 Typy minionů v nabídce

Battlegrounds nabízí v každé lobby jen podmnožinu typů minionů, ale nikde ji nevypíše
dopředu. Tracker ji proto skládá z ras karet, které se skutečně objevily v nabídce Boba.
Karta se započítá, jen když splňuje všechno z tohoto:

- má `IS_BACON_POOL_MINION` a `CARDTYPE=MINION`;
- vznikla mimo fázi souboje;
- vznikla rovnou v zóně `PLAY` na soupeřově straně, tedy v řadě nabídky;
- má pozici větší než nula.

Poslední dvě podmínky jsou podstatné. Karta vyrobená efektem jiné karty se rodí u svého
hráče v `SETASIDE` a teprve pak se může objevit v řadě nabídky; podle pozdějšího stavu by
se od skutečné nabídky nedala odlišit. Rasa `ALL`, tedy Amalgám, se nezapočítává vůbec.

Seznam se plní za běhu a v pozorovaných hrách byl kompletní zhruba do pátého kola.

Přesný seznam ale z `Power.log` získat nelze. Existují karty patřící do víc poolů zároveň:
`BG31_330` Ominous Seer má `CARDRACE=NAGA`, ale zároveň `BACON_SUBSET_DEMON`
i `BACON_SUBSET_NAGA`. V démonní lobby bez nág se tedy může objevit v nabídce a přidá typ
navíc. Z těchto dvou důvodů je panel v UI popsaný jako „typy v nabídce“, ne jako oficiální
seznam lobby.

### 8.6b Bonusy platné pro celou hru

Efekty jako „vaše tavern kouzla dávají +1/+1 víc“ nebo „blood gemy dávají +1/+1 víc“ se
nikam na minion nezapisují. Hra si je drží jako čísla na **entitě hráče**, takže se čtou
stejně jako zlato:

| Co | Tag útoku | Tag života |
| --- | --- | --- |
| Tavern kouzla | `TAVERN_SPELL_ATTACK_INCREASE` | `TAVERN_SPELL_HEALTH_INCREASE` |
| Blood gemy | `BACON_BLOODGEMBUFFATKVALUE` | `BACON_BLOODGEMBUFFHEALTHVALUE` |
| Elementálové | `BACON_ELEMENTAL_BUFFATKVALUE` | `BACON_ELEMENTAL_BUFFHEALTHVALUE` |
| Piráti | `BACON_PIRATE_BUFFATKVALUE` | `BACON_PIRATE_BUFFHEALTHVALUE` |

Hodnota je vždy kumulativní součet za celou hru, ne přírůstek za jeden efekt, takže se
jen zrcadlí do `TrackerState.Buffs`. V pozorovaném zápase vylétla po opakovaných
battlecry Red Chromadrake s Brannem až na `+30/+22`.

Jeden háček: **na začátku každého souboje hra všechny tyhle tagy vynuluje a o tři sekundy
později je vrátí na starou hodnotu.** Nulování přichází už s `BACON_IN_COMBAT_PHASE=1`,
takže kdyby se zrcadlilo, počítadlo by v každém souboji na několik sekund spadlo na
`+0/+0`. Návrat na nulu se proto zahodí, dokud počítadlo drží vyšší hodnotu — v jedné hře
tyhle bonusy jen přibývají a skutečnou nulu nastaví až nová hra přes `BeginGame()`.

V UI je řádek `Bonusy: …` pod výpisem typů a ukazuje jen to, co v dané hře skutečně
nastalo; když nic, řádek se schová a nebere místo.

### 8.7 Souboje a konečné umístění

Přepnutí `BACON_IN_COMBAT_PHASE` na 1 zakládá nový `CombatRound` s číslem kola a slotem
soupeře z `NEXT_OPPONENT_PLAYER_ID`. Výsledek se doplňuje až v následující nákupní fázi:

- `BACON_WON_LAST_COMBAT=1` znamená výhru;
- kladné `DAMAGE_DEALT_TO_HERO_LAST_TURN` znamená prohru a zapíše utrpěné poškození;
- souboj, který do začátku dalšího souboje výsledek nedostane, se uzavře jako remíza.

Konečné umístění se při `FINAL_GAMEOVER` bere z `PLAYER_LEADERBOARD_PLACE` lokálního hrdiny.
Konec hry se hlásí jednou: umístěním, a jen když ho log nedá, holým výsledkem.

### 8.8 Režim Duos

Duos pozná tracker podle tagů `BACON_DUO_*` a přepne se do `TrackerState.IsDuos`. Osm hráčů
tvoří čtyři dvojice a rozdávají se čtyři místa, ne osm (`PlaceCount`).

| Tag | Kde | Význam |
| --- | --- | --- |
| `BACON_DUO_TEAM_ID` | entita hrdiny, u lokálního hráče entita hráče | tým 1 až 4 |
| `BACON_DUO_TEAMMATE_PLAYER_ID` | entita lokálního hráče | slot spoluhráče |
| `NEXT_OPPONENT_TEAMMATE_PLAYER_ID` | entita lokálního hráče | druhý ze soupeřící dvojice |

Dvojice **nejsou sousední sloty**. V pozorovaném logu tvořily tým sloty 3 a 8, takže se
partner bez tagu odvodit nedá. Vlastní tým navíc hra napíše jinam než u ostatních: na entitu
hráče, ne na entitu hrdiny. `LinkLocalTeam` proto číslo doplní lokálnímu hráči i jeho
spoluhráči, ať přišlo z kterékoli strany.

I v Duos se nastupuje proti **jedinému** soupeři; druhého z dvojice si bere spoluhráč a v logu
po jeho souboji nezůstane nic. Ověřeno měřením u prvního útoku: na soupeřově straně stojí vždy
jen jedna deska. `NEXT_OPPONENT_TEAMMATE_PLAYER_ID` je proto doplňková informace o dvojici, ne
druhý protivník, a hláška o souboji jmenuje jen toho, proti komu se bojovalo. Desku druhého
z dvojice tracker uvidí až v kole, kdy proti němu nastoupí sám.

Hlášky o souboji nesou číslo kola. Výsledek dorazí až v další nákupní fázi a u remízy dokonce
až se začátkem dalšího souboje, takže by jinak visel pod nadpisem cizího kola.

`PLAYER_LEADERBOARD_PLACE` nese v Duos umístění **týmu**, tedy 1 až 4, a oba spoluhráči mají
stejné. Řadit hráče jednotlivě by dvojice roztrhalo, proto `TrackerState.Teams` seskupí lobby
podle `TeamId`, týmy seřadí podle součtu zbývajících životů a uvnitř týmu dá dopředu lokálního
hráče. Číslo místa se pak píše jen k prvnímu z dvojice.

Tým padá až s druhým hrdinou. U prvního mrtvého spoluhráče by ohlášení umístění tvrdilo, že
tým skončil, i když hraje dál, takže se hlásí jen `X vypadl, tým hraje dál.`

### 8.9 V Duos nese HERO_ENTITY cizí hrdiny

Entit hráče je v logu vždycky jen pár: lokální a jedna sdílená soupeřova. Jméno té soupeřovy
se mění podle toho, koho klient zrovna ukazuje. V sólu to nevadí, protože se v jednom souboji
nastupuje proti jedinému hrdinovi. V Duos jsou hrdinové dva a `HERO_ENTITY` se během souboje
přepne na oba, ale pod jedním jménem:

```text
TAG_CHANGE Entity=Myšpulín#21600 tag=HERO_ENTITY value=682    (PLAYER_ID=2, spoluhráč)
TAG_CHANGE Entity=Zullaman       tag=HERO_ENTITY value=686    (PLAYER_ID=8)
TAG_CHANGE Entity=Myšpulín#21600 tag=HERO_ENTITY value=107    (PLAYER_ID=1, zpět na sebe)
TAG_CHANGE Entity=Zullaman       tag=HERO_ENTITY value=740    (PLAYER_ID=3)
```

Bez obrany obsadil jeden BattleTag dva sloty: v tabulce se čtyřikrát opakovala stejná jména
a skutečná jména spoluhráčů se ztratila. Proto platí, že **jeden BattleTag může vlastnit jen
jeden slot**; druhá a další vazba téhož jména se zahodí. Na skutečném logu to dá správné
přiřazení u šesti z osmi slotů, zbylé dva log nikdy nepojmenuje a zůstanou jako
`Skrytý hráč`.

Samo o sobě to ale nestačí, protože o vítězi rozhoduje pořadí v logu. Kdo z dvojice bojuje
první, řídí `BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT`, a když jde první spoluhráč, dorazí
`HERO_ENTITY` na jeho hrdinu dřív než na vlastního. Spoluhráč by tak zabral jméno i slot
lokálního hráče, dostal příznak `IsLocal`, a s ním i cizí živou desku. Vlastní BattleTag
proto smí obsadit jen slot, jehož `PLAYER_ID` sedí na entitu lokálního hráče.

Ze stejného důvodu se živá deska nepřiřazuje podle `IsLocal`, ale podle `LocalPlayerSlot`:
spoluhráč sdílí v Duos tutéž stranu desky (`CONTROLLER`) jako lokální hráč, takže samotný
příznak je na rozlišení příliš slabý.

Deska spoluhráče se v logu samostatně neobjeví. Ověřeno měřením: mimo souboj je na straně
lokálního hráče vždycky nanejvýš sedm minionů, tedy jen vlastní deska. Podokno u spoluhráče
to říká rovnou, místo aby slibovalo desku, až proti němu uživatel nastoupí — proti
spoluhráči se nenastupuje nikdy.

## 9. Klíčové poznatky z reálného Battlegrounds logu

### 9.1 Lobby se nesmí klíčovat podle CONTROLLER

Nejdůležitější zjištění při živém testování bylo, že osm míst v Battlegrounds lobby je
spolehlivěji identifikováno tagem `PLAYER_ID` přímo na entitě hrdiny. Pozorované hodnoty
byly 1 až 8. `CONTROLLER` v témže logu nepředstavoval osm lobby slotů; opakovaly se
například jiné interní hodnoty. Původní přístup založený na controlleru proto ukazoval
jen lokálního hráče a některé pomocné entity.

Aktuální algoritmus vyžaduje:

1. entitu rozpoznanou jako hrdina;
2. její `PLAYER_ID`, který určí lobby slot;
3. odhalené jméno entity pro název hrdiny.

Hrdina je rozpoznán přes `CARDTYPE=HERO` nebo Card ID obsahující `HERO`, s vyloučením
`HERO_POWER`.

### 9.2 Vazba BattleTagu na hrdinu

Vazba jména hráče na hrdinu se v pozorovaném logu objevuje ve tvaru:

```text
TAG_CHANGE Entity=<BattleTag> tag=HERO_ENTITY value=<heroEntityId>
```

Tracker si proto vede mapu `heroEntityId -> BattleTag`. Jakmile má stejná hero entita
také `PLAYER_ID`, lze BattleTag připojit ke správnému lobby slotu. Pořadí řádků není
pevné: jméno, Card ID, `PLAYER_ID`, statistiky a `HERO_ENTITY` mohou přijít v různém
pořadí. `EntityMetadata` proto údaje průběžně skládá a `SyncLobbyHero` se volá po každém
relevantním doplnění.

BattleTag soupeře nemusí být vždy odhalen. V takovém případě UI zobrazuje
`Skrytý hráč`. To není chyba zarovnání ani parseru, pokud vazba v logu skutečně chybí.

### 9.3 Lokální hráč

Řádek deklarace hráče obsahuje `GameAccountId=[hi=... lo=...]`. Nenulová kombinace byla
v pozorovaném logu použita jako signál lokálního hráče. Následný řádek `PlayerName`
dodá BattleTag. Lokální hráč je v UI řazen jako první, ostatní podle `PlayerId`.

Výsledek celé hry se přebírá z `PLAYSTATE` lokálního hráče. Výsledky ostatních entit se
mohou objevit v posledních událostech, ale nesmí přepsat hlavní výsledek lokálního
uživatele.

### 9.4 Bartender a pomocné entity

V logu existují hero-like a další pomocné entity, včetně Bartender Boba. Aby se
nezobrazovaly jako hráči lobby, tracker filtruje jméno `Bartender Bob` a Card ID
`TB_BaconShopBob`. Obecné `ObservedParticipant` může stále obsahovat více technických
entit; desktopová tabulka ale používá pouze `LobbyParticipants`.

### 9.5 MMR

Datový model má `LobbyParticipant.Mmr`. V celém pozorovaném logu se nevyskytuje jediný
řetězec `rating` ani `mmr` a žádný tag této hodnotě neodpovídá. Sloupec MMR byl proto
z UI odstraněn a nahrazen počtem triplů. Budoucí implementace musí nejprve doložit zdroj
z logu nebo jiného povoleného lokálního API.

### 9.6 GameState je stav, PowerTaskList je animace

Log má dva paralelní proudy. `GameState.DebugPrintPower()` je autoritativní stav hry.
`PowerTaskList.DebugPrintPower()` je fronta animací, kterou hra vypisuje se zpožděním
i několika sekund a která obsahuje stav platný v době zařazení do fronty. Kdyby se z ní
braly zóny a pozice, vracely by se na desku už odstraněné karty.

Tracker proto z odložených řádků přebírá pouze dvě věci:

1. jména entit, protože ta se v době přehrání fronty už znají;
2. vazbu `HERO_ENTITY`, protože jméno soupeře se objeví právě až tam.

### 9.7 Descriptor popisuje stav před změnou

Řádek

```text
TAG_CHANGE Entity=[entityName=Clunker Junker id=14297 zone=PLAY zonePos=1 ...] tag=ZONE value=REMOVEDFROMGAME
```

má v descriptoru `zone=PLAY`, přestože karta právě mizí ze hry. Hra navíc tentýž zastaralý
descriptor opakuje na desítkách dalších řádků. Z descriptoru se proto přebírá jen Card ID;
zóna, pozice a controller se čtou výhradně z explicitních tagů `ZONE`, `ZONE_POSITION`
a `CONTROLLER`.

### 9.8 Deska se každé kolo přegeneruje

Battlegrounds nepoužívá pro desku trvalé entity. Při každém přepnutí fáze souboje vzniknou
pro tytéž miniony nové entity a ty staré už log nikdy nezmíní – neodejdou ze zóny `PLAY`,
jen zůstanou viset. V jedné třináctikolové hře tak vznikne přes patnáct tisíc entit.

`TrackerState.Epoch` se proto zvyšuje s každým přepnutím `BACON_IN_COMBAT_PHASE` a každá
dotčená entita se aktuální epochou orazítkuje. Projekce desky, nabídky a ruky berou jen
entity z aktuální epochy. Bez toho by deska po třinácti kolech obsahovala osmdesát minionů
místo sedmi.

### 9.9 Soubojová kopie hrdiny nesmí křísit lobby

Pro každý souboj vznikne dočasná kopie hrdiny s tagem `BACON_COMBAT_PHASE_HERO=1`. Má
stejné `PLAYER_ID`, ale znovu plné `HEALTH` a nulové `DAMAGE`. Trvalý hrdina žebříčku,
poznatelný podle `PLAYER_LEADERBOARD_PLACE`, drží skutečný průběžný stav.

Slot v lobby si proto nárokuje ta entita, která dostala `PLAYER_LEADERBOARD_PLACE`, a jen
z ní se čte HP, armor, tier, pořadí a triply. Soubojová kopie smí doplnit pouze jméno
hrdiny a BattleTag. Bez tohoto rozlišení se dávno vyřazení hráči po souboji s nimi zase
objevili jako živí s třiceti životy.

### 9.10 Pořadí se počítá ze zbývajících životů

Tag `PLAYER_LEADERBOARD_PLACE` se v logu obnovuje po dávkách a pro některé hráče zůstává
dlouho zastaralý. `TrackerState.Standings` proto pořadí počítá sám: živí hráči sestupně
podle `HP - DAMAGE + ARMOR`, pod nimi vyřazení podle svého konečného umístění. Číslo v UI
je pozice v tomto seznamu, ne hodnota z logu.

### 9.11 Jméno soupeře přebíjí jméno Bobova skinu

Vazba BattleTagu se v logu objevuje i ve tvaru

```text
TAG_CHANGE Entity=Winter Queen tag=HERO_ENTITY value=10159
```

kde `Winter Queen` není hráč, ale skin Bartendera Boba. Hra tímto jménem dočasně nahrazuje
jméno soupeře, které ještě nezná. BattleTag se proto uzná jen tehdy, pokud nejde o zobrazované
jméno nějaké nehráčské entity. Správné jméno dorazí o pár sekund později z `PowerTaskList`.

### 9.12 BACON_SUBSET není seznam dostupných typů

Tagy `BACON_SUBSET_MECH`, `BACON_SUBSET_MURLOC` a další vypadají jako seznam typů v lobby,
ale nejsou to ony. Jde o zařazení konkrétní karty do poolů, a jedna karta jich může mít
víc: mechanický `BG_DEEP_015` má vedle `BACON_SUBSET_MECH` také `BACON_SUBSET_UNDEAD`,
přestože nemrtví v té lobby nebyli. Dostupné typy se proto čtou z `CARDRACE` minionů
označených `IS_BACON_POOL_MINION`, jak popisuje kapitola 8.6.

### 9.13 Tag TURN nese i entita hráče

Kromě `GameEntity` má tag `TURN` i entita lokálního hráče, kde jde o počet jeho vlastních
tahů. Ten roste zhruba poloviční rychlostí a bez omezení na `GameEntity` by kolo v UI
zamrzlo přibližně na polovině skutečné hodnoty.

## 10. Samostatný log každého zápasu a checkpoint

### 10.1 Proč se neupravuje Blizzard Power.log

Mazání nebo zkracování aktivního `Power.log` by bylo křehké: soubor vlastní Hearthstone,
může být otevřený pro zápis a hra může měnit jeho umístění nebo formát. Aktuální řešení
proto kombinuje dvě věci:

- byte checkpoint, aby se velký zdrojový log nemusel při každém startu číst celý;
- vlastní surový archiv právě probíhajícího zápasu, ze kterého lze obnovit stav.

Tím se řeší výpočetní problém velkého session logu, aniž by tracker zasahoval do souboru
hry.

### 10.2 Umístění dat

```text
%LOCALAPPDATA%\BattlegroundsTracker\
  checkpoint.json
  matches\
    match-yyyyMMdd-HHmmss-fff.power.log.br     (dohraný, zabalený)
    match-yyyyMMdd-HHmmss-fff.power.log        (jen ten právě rozehraný)
    ...
  cardart\
    BG31_035.jpg
    ...
  cards\
    cardtext.json
```

Složky `cardart` a `cards` jsou jen mezipaměti podle kapitoly 11.6. Smazat se dají kdykoli;
aplikace si kresby i popisy stáhne znovu.

### 10.2.1 Komprese a retence

Syrový log jednoho zápasu má desítky megabajtů; za jeden večer hraní narostla složka na
713 MB. Dohraný zápas se proto zabalí Brotli a rozehraný zůstává v prostém textu, protože se
z něj po restartu obnovuje. Naměřeno na skutečném zápase o 64,4 MB:

| kodek | velikost | poměr | zabalit | rozbalit |
| --- | --- | --- | --- | --- |
| GZip Optimal | 2,98 MB | 21,6× | 353 ms | 51 ms |
| GZip SmallestSize | 2,86 MB | 22,5× | 796 ms | 52 ms |
| Brotli Fastest | 3,14 MB | 20,5× | 44 ms | 56 ms |
| **Brotli Optimal** | **2,22 MB** | **29,0×** | **152 ms** | 53 ms |
| Brotli SmallestSize | 1,46 MB | 44,1× | 119 364 ms | 53 ms |

Brotli Optimal je menší i rychlejší než kterýkoli stupeň gzipu. Nejsilnější stupeň sice dá
44×, ale dvě minuty práce uprostřed hraní za pár megabajtů nestojí.

Filtrovat log na řádky, které parser čte, se nevyplatí: ubere 29 % řádků a jen 20 % místa,
protože objem tvoří `GameState` řádky, které se stejně potřebují. Se zabalením je rozdíl
1,70 MB proti 2,22 MB. Za půl megabajtu na zápas nestojí za to přijít o možnost archiv znovu
přečíst vylepšeným parserem — dnes ignorovaný řádek může zítra nést informaci navíc.

Drží se posledních `MatchLogArchive.RetainedMatches` dohraných zápasů, tedy pět. Ořez i dobalení
pozůstalých prostých logů proběhnou při otevření archivu, takže se složka srovná i po
aktualizaci ze starší verze nebo po pádu aplikace.

Čtení řeší `MatchLogArchive.ReadMatch`, které pozná zabalený soubor podle přípony. Volající
tak nemusí vědět, v jakém stavu zápas je.

### 10.2.2 Pořadová přípona

Přípona s pořadovým číslem se použije pouze při kolizi časového názvu. Soubory obsahují
původní řádky `Power.log` náležející zápasu. Proto mohou obsahovat BattleTagy a další
údaje z logu a mají být považovány za lokální uživatelská data.

Checkpoint má tento význam:

```json
{
  "SourcePath": "C:\\...\\Power.log",
  "SourcePosition": 12345678,
  "ActiveMatchFile": "C:\\...\\matches\\match-....power.log"
}
```

`ActiveMatchFile` je `null`, pokud poslední zápas skončil.

### 10.3 Start a obnovení

Při otevření live režimu proběhne následující algoritmus:

1. Zavře se případný předchozí archive writer.
2. Vytvoří se čistý parser a `GameStateTracker`.
3. Otevře se `MatchLogArchive` pro konkrétní zdrojový `Power.log`.
4. Pokud checkpoint ukazuje na nedokončený existující zápasový soubor, tento malý
   archiv se přehraje do čistého trackeru bez opětovného zápisu do archivu.
5. Pokud replay ukáže, že soubor ve skutečnosti obsahoval `FINAL_GAMEOVER`, aktivní
   příznak se opraví a archiv se uzavře.
6. `PowerLogTailReader` začne na `SourcePosition` a dočte pouze data přidaná od
   posledního checkpointu.
7. Po každé dávce se uloží nová pozice.

Checkpoint se přijme pouze tehdy, když odpovídá stejné absolutní cestě zdroje, zdroj
existuje a uložená pozice není za aktuálním koncem souboru. Poškozený JSON nebo dočasná
I/O chyba způsobí bezpečný fallback: checkpoint se ignoruje a zdroj se jednou přehraje
od začátku.

### 10.4 Začátek a konec zápasového souboru

Rozhodování drží `MatchRecorder.Handle`, aby stav a archiv nemohly rozejít:

- Soubor vzniká ve chvíli, kdy `GameStateTracker` skutečně začne novou hru, tedy když se
  zvýší `GamesSeen`. Případný předchozí soubor se předtím uzavře, i když jeho
  `FINAL_GAMEOVER` nikdy nedorazil.
- Každý další řádek aktivní hry se ihned zapisuje s `AutoFlush=true`.
- Jakmile hra přestane být aktivní, writer se zavře a `ActiveMatchFile` se vymaže.
- Mezi zápasy se řádky čtou a checkpoint se posouvá, ale nikam se nezapisují.
- Další soubor nevzniká prázdný bezprostředně po konci; vytvoří se až při skutečném
  `CREATE_GAME` následující hry.

Checkpoint se zapisuje přes dočasný `.tmp` soubor a následný replace/move. Opakovaný
timer tick bez změny byte pozice nebo aktivního zápasu checkpoint nepřepisuje, aby
nevznikaly zbytečné zápisy několikrát za sekundu.

### 10.5 První spuštění a velikost dat

Pokud ještě checkpoint neexistuje, desktop jednou projde celý aktuální `Power.log`.
Obsahuje-li session více her, mohou při tomto prvním importu vzniknout samostatné
archivy nalezených her. Další start už pokračuje od checkpointu.

Současné řešení omezuje cenu opakovaného parsování, ale samo automaticky nemaže ani
nekomprimuje dokončené zápasové soubory. Celkový adresář `matches` tedy může časem růst.
Vhodné budoucí rozšíření je:

- po konci hry komprimovat archiv do gzip;
- přidat nastavitelnou retenci, například 30 nebo 90 dní;
- případně uchovávat jen strukturované shrnutí a raw log mazat po úspěšném zpracování.

## 11. Desktopové UI

### 11.1 Charakter okna

Overlay je bezrámové WPF okno s průhledným okolím, vlastním tmavým designem a
`Topmost=true`. Ikona okna i `.exe` je `assets/bg-tracker.ico`; v hlavičce overlaye je
stejná značka „BG“ vykreslená přímo v XAML. Má vlastní tlačítko zavření, sbalení a resize grip. Okno lze táhnout za
horní lištu.

Dvojklik na horní lištu nebo tlačítko `−` overlay sbalí na pruh hlavičky: obsah se skryje,
okno si nechá šířku a zmenší se na výšku hlavičky, resize se vypne a hlavička se zakulatí
i dole, protože je pak celou kartou. Rozbalení vrátí předchozí velikost i možnost resize.

Sbalení musí kromě skrytí obsahu snížit i **návrhovou** výšku karty na 64 bodů. `Viewbox`
škáluje celou kartu, takže dokud má karta plnou návrhovou výšku, udělá z ní nízké okno jen
zdrobnělou miniaturu místo hlavičky v původní velikosti. Se stejnou výškou musí spadnout
i `MinHeight` okna, který by ho na výšku hlavičky vůbec nepustil.

Běžné topmost WPF okno funguje spolehlivě nad windowed nebo borderless fullscreenem.
Nad exkluzivním fullscreen režimem Windows nemusí overlay zobrazit. To je omezení typu
okna, nikoli parseru.

### 11.2 Poměrová velikost a pravidlo bez scrollu

Rozložení se sází v návrhových jednotkách 500 × 1163 a celý obsah je zabalený do `Viewbox`
se `Stretch="Uniform"`. Okno se pak nastaví na návrhovou velikost vynásobenou zvětšením, takže
overlay zabere stejný podíl obrazovky na FullHD i na 4K a nic se neořezává ani nescrolluje.

Zvětšení se počítá z **výšky pracovní plochy**, ne z počtu pixelů:

```text
zvětšení = clamp(výška pracovní plochy × 0,94 / návrhová výška, 0,70, 1,30)
```

Pixely by byly špatná míra, protože `SystemParameters.WorkArea` je v jednotkách nezávislých na
DPI. Na 4K se 200% zvětšením Windows už jednou zvětšily samy a druhé násobení podle pixelů by
overlay nafouklo na dvojnásobek. Naměřené hodnoty:

| pracovní plocha | zvětšení | okno |
| --- | --- | --- |
| 900 (malý notebook) | 0,73 | 364 × 846 |
| 1040 (FullHD) | 0,84 | 420 × 978 |
| 1392 | 1,13 | 563 × 1308 |
| 1800 (4K při 100 %) | 1,30 | 650 × 1512 |

Meze zvětšení jsou tam kvůli čitelnosti: bez dolní by se na malém monitoru zdrobnila písmena
k nečitelnosti, bez horní by overlay na velkém zabral půl obrazovky. Když by plné rozložení
vyžadovalo zvětšení pod 0,85, sekce desek se při startu sbalí sama a zbytek se vejde
v čitelnější velikosti.

#### Původní pravidlo bez scrollu

Výška se při každém vytvoření okna aktivně vypočítá:

```text
min(1163, max(640, výška pracovní plochy - 24))     rozbalené
min(879,  max(640, výška pracovní plochy - 24))     sbalené
```

Šířka je 500 a minimální šířka 440. Návrhové výšky pokrývají nejhorší možný obsah:
osm hráčů lobby, sedm minionů vlastní desky, sedm karet v nabídce Boba nebo na desce
soupeře a šest posledních událostí.

Seznamy lobby, desky a nabídky jsou `ItemsControl`, nikoli `ListBox`, takže scrollovat
principiálně nemohou. Místo pro maximální počet položek si rezervují napevno přes
`MinHeight`, aby overlay při změně počtu minionů neposkakoval.

Jediný scrollovatelný panel je `POSLEDNÍ UDÁLOSTI`. Ten zůstává interaktivní, aby v něm
fungovalo kolečko myši i tažení scrollbaru.

Právě proto má jeho řádek malou `MinHeight` (60), zatímco ostatní seznamy si místo rezervují
napevno. Výška karty je pevná, takže když se nad spodní lištou objeví pruh s načítáním
zápasu nebo s nabídkou aktualizace, přebytek se nemá kam vejít a mřížka odřízne poslední
řádek — tedy tlačítka. Události mají scrollbar, a tak jsou jediný panel, který se smí
zmenšit; s `MinHeight="159"` se lišta při načítání vybraného logu rozbíjela.

Rozložení má tři návrhové výšky: 1163 bodů s rozbalenými deskami, 879 se sbalenými
a 64 pro sbalený overlay, tedy samotný pruh hlavičky (viz 11.1).
Ta první se nevejde na monitor s rozlišením 1920×1080, kde po odečtení hlavního panelu
zbývá kolem 1010 bodů. Proto jde sekce s deskami sbalit klikem na její nadpis.
**Když je při startu k dispozici méně místa, než potřebuje plné rozložení, sekce se sbalí
sama**, aby uživatel nepřišel o spodek okna s ovládacími tlačítky.

### 11.3 Obsah

Horní karty zobrazují kolo, aktuální místo v žebříčku, zlato a fázi.

Pod nimi je řádek s dalším soupeřem a cenou upgradu tavernu, následovaný tabulkou lobby
se sloupci:

- `#` – 20, pořadí spočítané ze zbývajících životů;
- `HRDINA / BATTLETAG` – flexibilní zbytek šířky;
- `HP` – 32;
- `ARMOR` – 40;
- `TIER` – 30;
- `TRIPLE` – 42, počet dosažených triplů.

Stejné grid šířky používá hlavička i každý datový řádek, proto jsou hodnoty přesně pod
nadpisy. Název hrdiny a BattleTag se při nedostatku šířky oříznou ellipsis.

Nad tabulkou je ještě řádek s typy minionů, které se objevily v nabídce Boba.

Řádek lobby je barevně odlišený: modrý pro lokálního hráče, zelený pro spoluhráče v Duos,
okrový pro dalšího soupeře. V Duos jsou dvojice oddělené mezerou a číslo místa se píše jen
k prvnímu z nich, protože místo patří týmu; podrobnosti v kapitole 8.8. Karta `MÍSTO` ukazuje
`3/4` v Duos a `5/8` v sólu a v hlavičce okna přibude vedle režimu čtení popisek `DUOS`.
Vyřazený hráč je ztlumený, má lebku před jménem hrdiny a místo HP křížek.

Načítání uloženého zápasu běží na pozadí a nad tlačítky se po tu dobu ukazuje pruh s postupem.
Půl milionu řádků se parsuje pár sekund a na vlákně rozhraní by okno mezitím zamrzlo. Postup se
počítá z pozice v souboru na disku, protože počet řádků se u zabaleného zápasu předem zjistit
nedá. V hlavičce se místo jména souboru ukazuje jen datum a čas zápasu, celá cesta je
v podokně; jméno archivu má přes čtyřicet znaků a z hlavičky přetékalo.

Po najetí myší na řádek se vlevo od overlaye otevře podokno s deskou daného hráče. Miniony
v něm nejsou řádky, ale kartičky vedle sebe podle kapitoly 11.6.
U lokálního hráče jde o živou desku, u ostatních o poslední, kterou log ukázal, s číslem
kola. Dokud jste proti hráči nenastoupili, podokno to řekne místo prázdného seznamu.

Následuje karta s vlastní deskou a pod ní buď `NABÍDKA BOBA`, nebo `DESKA SOUPEŘE` podle
toho, jestli právě běží souboj. Každý řádek ukazuje pozici, hvězdičku u zlaté karty,
jméno, klíčová slova celým jménem (`Taunt`, `Divine Shield`, `Reborn`, `Venomous`,
`Windfury`), útok u ikony meče, život u ikony srdce a tavern tier jako číslo s hvězdičkou.
Statistiky nad tisíc se krátí přes `StatFormat.Compact` na `1k` nebo `2,4k`; v pozdních kolech
mají čtyři místa a do sloupců ani na kartičku se nevejdou. Zaokrouhluje se vždy dolů, aby
zkratka netvrdila víc, než minion doopravdy má.
Sloupce drží stejnou šířku napříč řádky přes `SharedSizeGroup`; bez něj si každý řádek
měří `Auto` sloupce sám a hodnoty se nezarovnají pod sebe.

Po najetí myší na řádek se vlevo ukáže tatáž kartička jako v podokně hráče, jen zvětšená
transformací na dvojnásobek, a pod ní řádek s typem minionu, pozicí a celými klíčovými
slovy. Do kartičky se klíčová slova vejdou jen oříznutá, protože má pevnou šířku.

Klik na nadpis `MOJE DESKA` celou tuhle kartu sbalí a okno se o její výšku zmenší. Slouží
to monitorům, na které se plné rozložení nevejde; podrobnosti v kapitole 11.2.

Panel `POSLEDNÍ UDÁLOSTI` se drží toho, co se týká vlastního hrdiny, respektive vlastního týmu
v Duos. Souboj hlásí kolo, soupeře, výsledek a poškození: `Kolo 12 · tým vs Vanndar Stormpike:
výhra, dal jsem 34 dmg.` Dané poškození log nehlásí zvlášť, počítá se z přírůstku tagu `DAMAGE`
na soupeřově hrdinovi za dobu souboje. Vyřazení jmenuje **hrdinu**, ne hráče: hrdina se pamatuje
lépe a v Duos ho log zná i u hráčů, jejichž BattleTag nikdy neodhalí.

Výsledek souboje se dozvídáme po částech — nejdřív jestli se vyhrálo, pak poškození jedné
a druhé strany — a mezi tím se do panelu vejdou i jiné události. `TrackerState.UpdateEvent`
proto přepisuje už zveřejněnou hlášku na jejím místě; přepis jen poslední položky nestačil,
protože se mezi ni a doplněk vešlo vyřazení hráče.

Panel zobrazuje události od nejnovější po nejstarší. `TrackerState`
uchovává frontu maximálně šesti položek; po přidání sedmé zahodí nejstarší. Panel je na
šest řádků navržený, takže se scrollbar objeví jen u delších zalomených textů.

V hlavičce je verze jako odznáček hned za názvem aplikace. Jeho výška je svázaná
s `ActualHeight` titulku, takže sedí na stejné lince i kdyby se velikost názvu změnila.
Ladicí build se pozná i barvou: okrový rámeček a okrové písmo místo tlumeného. Bez toho se dá snadno hodinu ladit něco, co
vůbec neběží — přesně to se stalo, když vydaný Release zůstal o dva dny starší než opravený
Debug.

Dolní část obsahuje stav, výsledek a diagnostiku počtu zpracovaných řádků a rozpoznaných
událostí. Vpravo na témže řádku jsou dvě ikony: patch notes a ovládání. Ovládání se
otevírá jako menu s položkami:

- **Vybrat log…** – standardní file dialog;
- **Spustit demo** – reset a syntetická data;
- **Pozastavit / Pokračovat** – zastaví nebo spustí timer;
- **Restart** – restartuje aktuální režim.

Původně to byla čtyři tlačítka na samostatném řádku, který v pevné výšce karty zabíral
skoro 40 px. Menu se umisťuje přes `CustomPopupPlacementCallback` pravou hranou k tlačítku
a nad něj: tlačítko sedí u pravého dolního rohu okna, takže výchozí umístění by menu
poslalo mimo okno a WPF ho na širší ploše nemá kam odrazit.

Ikony jsou glyfy z fontu `Segoe MDL2 Assets`, ale **nesmí být pouhým `Content` tlačítka**.
`ContentPresenter` z textového obsahu vyrobí `TextBlock`, na který se vztahuje implicitní
styl `TextBlock` z `App.xaml`, a ten nastavuje `FontFamily="Segoe UI"`. Setter ze stylu
přebije font zděděný z tlačítka, takže by se z glyfu stal prázdný rámeček. Proto je uvnitř
tlačítka vlastní `TextBlock` se stylem `GlyphStyle`.

#### Okno s patch notes

Ikona patch notes otevře `PatchNotesWindow` s vestavěným prohlížečem `WebView2`. Okno je
`Topmost` jako overlay, takže drží nad hrou, a má vlastní chrome ve stylu aplikace.

Chování okna zajišťuje `WindowChrome`, ne `WindowStyle="None"` s `AllowsTransparency`.
Druhá varianta nechá jen úchop v pravém dolním rohu a tažením se okno nezvětší vůbec;
`WindowChrome` dá změnu velikosti ze všech stran, přichytávání k okrajům i tažení za
titulní lištu. Dvě věci to vyžaduje:

- tlačítka v oblasti titulku potřebují `WindowChrome.IsHitTestVisibleInChrome="True"`,
  jinak kliknutí spolkne caption;
- **`WebView2` nesmí sahat až k hraně okna.** Je to skutečné dceřiné okno (HWND), takže
  by spolklo hit-test okrajů a okno by se tažením nedalo zvětšit. Proto má obal okraj
  6 px, tedy stejný jako `ResizeBorderThickness`.

Runtime WebView2 aplikace nevyžaduje. Na Windows 11 a všude s Edge je předinstalovaný;
když chybí, `PatchNotesWindow.Show` okno vůbec neotevře a odkaz pošle do systémového
prohlížeče. Totéž tlačítko je i v okně samotném a v chybovém hlášení, kdyby se prohlížeč
nepodařilo nastartovat. Data prohlížeče jdou do
`%LOCALAPPDATA%\BattlegroundsTracker\webview2`, protože výchozí složka leží vedle `.exe`,
kam nemusí být právo zápisu.

### 11.4 Aktualizace kolekcí

`MainViewModel` kolekce nepřepisuje přes `Clear` a znovunaplnění, ale porovnává je položku
po položce a mění jen to, co se skutečně liší. Kompletní přepis by při každém ticku
resetoval posun v panelu událostí a seznamy by problikávaly.

Položky jsou `record`, takže porovnání je strukturální a bez ručně psané rovnosti. Deska
uvnitř řádku lobby je výjimka: porovnává se podle reference, a view model proto vrací
stejnou instanci, dokud se obsah desky nezmění. Jinak by se řádek nahrazoval při každém
ticku a zavíral právě otevřené podokno.

### 11.5 Scrollbary

Styly scrollbarů jsou definované v `Tracker.Desktop/App.xaml`. Používají vlastní tmavé
pozadí, zaoblený thumb a barvy sladěné s kartami overlaye. Nativní světlý scrollbar se
proto v panelu událostí nepoužívá.

### 11.6 Kartičky minionů, kresby a popisy karet

Kartička je `MinionCardTemplate` v `MainWindow.xaml`, 148 × 214 bodů, a napodobuje kartu ze
hry: kresba je vystřižená do oválu s kovovým prstencem, takže kolem ní nezůstane obdélníkové
pozadí. Vlevo nahoře přes okraj portrétu leží štít s tavern tierem, vpravo nahoře hvězdička
u zlaté karty. Pod portrétem je jmenná páska, pod ní rámeček s klíčovými slovy a popisem
efektu, na jeho spodní hraně sedí páska s typem minionu a v dolních rozích jsou drahokamy
s útokem a životem. Čísla jsou v `Viewbox`, takže se i čtyřmístné hodnoty zmenší tak, aby se
do drahokamu vešly. Zlatá karta má zlatý prstenec i rámeček místo ocelového.

Karta má vlastní tmavý podklad. Bez něj se v řadě slily a nešlo poznat, ke které kartě patří
který drahokam. Skládají se do `WrapPanel`; šířka seznamu je nastavená přesně na sedm karet,
což je maximum na desce v Battlegrounds. Po najetí myší na řádek desky v okně se tatáž
šablona ukáže zvětšená transformací.

#### Kresba

Kresby nejde brát z instalace hry. Obrázky leží v `Data\Win\*.unity3d`, což je zhruba 12 GB
Unity asset bundlů; jejich čtení by vyžadovalo zvláštní parser, po každém patchi by se
rozpadlo mapování na Card ID a extrahovaný art Blizzardu nemá co dělat ve veřejném
repozitáři. Kresba se proto stahuje z veřejné CDN HearthstoneJSON:

```text
https://art.hearthstonejson.com/v1/256x/{CardID}.jpg
```

Hotové renderované karty s průhledným pozadím by ušetřily veškeré kreslení, ale žádný
veřejný zdroj je pro aktuální sety nemá. `art.hearthstonejson.com/v1/render/…` končí zhruba
u setu BG29: z měřeného vzorku čtyřiceti Card ID z reálných logů mělo render patnáct, kdežto
kresbu třicet sedm. Ty tři chybějící byly enchanty a pomocná karta `TB_BaconShop_DragSell`,
které se na desce nikdy nezobrazí. Prověřené a nefunkční alternativy: `static.zerotoheroes.com`
má jen `cardart`, `static.hsreplay.net` rendery nemá a oficiální knihovna karet Blizzardu
plní obrázky až v prohlížeči a k obrázkům přes `d15f34w2p8l1cc.cloudfront.net` je potřeba
OAuth, který by musel nastavovat každý uživatel zvlášť.

Kreslit rámeček a čísla vlastní šablonou má navíc jednu výhodu, kterou by hotový render
nedal: render nese základní statistiky karty, kdežto tracker zná ty skutečné, nabuffované.

`CardArtProvider` v `Tracker.Core` řeší stahování a mezipaměť:

- soubory jdou do `%LOCALAPPDATA%\BattlegroundsTracker\cardart\{CardID}.jpg`, jedna kresba
  má okolo 14 kB a celá jedna hra si jich vyžádá pár desítek;
- zapisuje se přes `*.part`, aby po nedokončeném stahování nezůstal v mezipaměti useknutý
  obrázek, který by se už nikdy nestáhl znovu;
- souběžné dotazy na stejnou kartu sdílejí jednu úlohu;
- odpověď 404 se zapamatuje natrvalo (enchanty kresbu nemají), ale výpadek sítě se
  zapomene, aby si aplikace o kresbu řekla znovu, až bude připojení zpátky;
- jméno souboru vzniká z Card ID, proto se přijímají jen písmena, číslice a podtržítko.

#### Popis efektu

Log nese jen Card ID a statistiky, žádný text. `CardTextProvider` proto stáhne databázi
karet HearthstoneJSON:

```text
https://api.hearthstonejson.com/v1/latest/enUS/cards.json
```

Ta má přes devět megabajtů nekomprimovaně a čte se proudem přes
`JsonSerializer.DeserializeAsyncEnumerable`, aby se celá nemusela materializovat v paměti.
Uloží se z ní jen dvojice Card ID a text pro karty s prefixem `BG` nebo `TB_Bacon`, což je
zhruba 4 500 karet a 360 kB v souboru
`%LOCALAPPDATA%\BattlegroundsTracker\cards\cardtext.json`. Po čtrnácti dnech se databáze
stáhne znovu; při výpadku sítě se použije i prošlá kopie, protože starý popis je pořád lepší
než žádný.

Na rozdíl od renderů je pokrytí textů úplné: ze 636 unikátních Card ID ve čtyřech reálných
zápasech byla v databázi všechna a 573 z nich mělo text. Zbylých 63 jsou vanilla minioni
a tokeny, které popis nemají ani ve hře.

`CardTextProvider.Clean` odstraní značky, kterými databáze řídí sazbu v klientu hry: úvodní
`[x]`, ruční zalomení řádků, jednoduché HTML a mřížky před čísly poškození. Z
`"[x]<b>Battlecry:</b> Deal $3\ndamage."` tak zbude `Battlecry: Deal 3 damage.`, které se
v kartičce zalomí samo podle šířky.

#### Sdílený držák

`CardCache` v `Tracker.Desktop` mapuje Card ID na `CardInfo`, což je držák kresby i popisu
s `INotifyPropertyChanged`. Pro každé Card ID existuje jediná instance, takže model pohledu
zůstane při porovnání stejný a doplnění dat nepřestaví celý seznam ani nezavře otevřené
podokno. Obrázek se dekóduje na pozadí s `BitmapCacheOption.OnLoad` a zmrazí, aby šel předat
do vlákna rozhraní.

Portrét je v oválu vystřižený z prostředních 74 % kresby, o kousek výš, protože postava na kartě
sedí v horní polovině. Bez toho koukaly v okrajích oválu světlé kraje čtvercové kresby.

Obojí se vyžádá už při složení modelu pohledu, ne až při najetí myší. Než uživatel na řádek
najede, jsou data zpravidla stažená. Když se stáhnout nepodaří, kartička se vykreslí na
tmavém podkladu, popis se vynechá a všechny ostatní údaje zůstanou čitelné.

## 12. Konzolová aplikace

Konzolový projekt je možné použít pro diagnostiku a jednoduchý replay:

```powershell
dotnet run --project src/Tracker.App -- --demo
dotnet run --project src/Tracker.App
dotnet run --project src/Tracker.App -- --log "D:\Hearthstone\Logs\Power.log"
dotnet run --project src/Tracker.App -- --replay --log ".\Power.log"
```

Parametry:

- `--log <cesta>` – explicitní zdroj;
- `--demo` – přiložený syntetický log a ukončení;
- `--replay` – jednorázové načtení a ukončení;
- `--help` nebo `-h` – nápověda.

Konzolová varianta sdílí `PowerLogParser` a `GameStateTracker`, ale stále má vlastní
starší `PowerLogReader` a `PowerLogLocator`. V současném stavu nepoužívá
`PowerLogDiscovery`, `PowerLogTailReader` ani `MatchLogArchive`. Obnova rozehrané hry a
samostatné zápasové soubory jsou tedy zapojené pouze v desktopové aplikaci.

Textový dashboard ukazuje stejná data jako overlay: žebříček, vlastní desku, nabídku Boba
nebo desku soupeře, ruku a historii soubojů. `--replay` nad archivem v
`%LOCALAPPDATA%\BattlegroundsTracker\matches` je nejrychlejší způsob, jak ověřit parser
proti skutečné hře; kompletní 45MB log se přehraje přibližně za tři sekundy.

Sjednocení obou vstupních cest na infrastrukturu z `Tracker.Core` je vhodný refactoring.

## 13. Spuštění a sestavení

### 13.1 Desktop ze zdrojového kódu

```powershell
dotnet run --project src/Tracker.Desktop
```

Explicitní log:

```powershell
dotnet run --project src/Tracker.Desktop -- --log "D:\Hearthstone\Logs\Power.log"
```

Ve Visual Studiu otevřít `HearthstoneBattlegroundsTracker.sln`, nastavit
`Tracker.Desktop` jako startup project a použít `F5` nebo `Ctrl+F5`.

### 13.2 Build a testy

```powershell
dotnet build HearthstoneBattlegroundsTracker.sln -c Release
dotnet test HearthstoneBattlegroundsTracker.sln -c Release --no-build
dotnet format HearthstoneBattlegroundsTracker.sln --verify-no-changes --no-restore
```

### 13.3 Samostatné desktopové EXE

```powershell
.\scripts\publish-gui.ps1
```

Výsledek pro výchozí runtime:

```text
artifacts\desktop\win-x64\BattlegroundsTracker.exe
```

ARM64 varianta:

```powershell
.\scripts\publish-gui.ps1 -Runtime win-arm64
```

Publish je Release, self-contained a single-file. Na cílovém počítači tedy nemusí být
samostatně instalovaný .NET runtime.

Skript navíc zapíná `IncludeNativeLibrariesForSelfExtract` a kompresi. Bez toho vznikne
vedle `.exe` ještě pět nativních knihoven WPF a samotný `.exe` poslaný dál nefunguje;
s tím je výsledkem jediný soubor a velikost klesne ze 155 MB na zhruba 69 MB. Vedle něj
vzniká `BattlegroundsTracker.exe.sha256`, který používá kontrola stažené aktualizace.

Konzolové EXE se publikuje pomocí `scripts\publish.ps1` do
`artifacts\publish\<runtime>`.

## 14. Verze a distribuce

### 14.1 Kde se verze udržuje a jak se pozná ladicí build

Číslo verze má jediný zdroj, `Version` v `Directory.Build.props`. Odtud ho MSBuild propíše
do `AssemblyVersion`, `FileVersion` i `InformationalVersion`, takže stejná hodnota je ve
vlastnostech souboru `.exe` i v aplikaci. Ve stejném souboru je i `Copyright`, který se
zobrazuje na obou místech.

`TrackerVersion` čte tyto atributy z knihovny `Tracker.Core`, ne ze spouštěného programu.
Všechny projekty dědí stejné `Version`, takže výsledek nezávisí na tom, jestli kód běží
v overlayi, v konzoli nebo pod testovacím hostitelem. Původní čtení z entry assembly
vracelo pod `dotnet test` verzi testhostu.

Verze je vidět v patičce overlaye, v hlavičce konzolového dashboardu a přes
`--version`.

Verze se drží v `VersionPrefix` v `Directory.Build.props`. Ladicí build k ní dostane příponu
přes `VersionSuffix`, takže hlásí `0.9.1-dev`, kdežto Release `0.9.1`. Vydává se jen Release, takže
spuštěná binárka nikdy netvrdí verzi, kterou nikdo nevydal.

`TrackerVersion` proto nabízí tři věci: `Current` s příponou pro zobrazení, `Numeric` bez ní pro
porovnání s vydáními na GitHubu (`Version.TryParse` by na `0.9.1-dev` selhalo) a `IsDevelopmentBuild`
pro odlišení v rozhraní.

#### Pravidla verzování

Projekt se drží [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html). Hlavní
číslo je nula, takže podle bodu 4 specifikace jde o počáteční vývoj a veřejné rozhraní se
smí měnit i mezi vedlejšími verzemi. Číslo se zvyšuje takto:

| Číslo | Kdy | Příklad z historie |
| --- | --- | --- |
| MAJOR | až s prvním stabilním rozhraním, tedy 1.0.0 | — |
| MINOR | nová funkce nebo viditelná změna chování | 0.8.0 počítadlo bonusů |
| PATCH | jen oprava, bez nové funkce | 0.2.1 místo pro šest řádků událostí |

Přípona předvydání se lepí přes `VersionSuffix`, tedy `0.8.0-dev` u ladicího buildu nebo
`0.9.0-rc.1` u tagu. Předvydání má podle specifikace nižší precedenci než tatáž verze bez
přípony a workflow ho na GitHubu označí jako prerelease, takže ho `releases/latest`
nenabídne uživatelům. Build metadata za znakem `+` doplňuje CI z commitu; do precedence se
nepočítají a do tagu nepatří.

Tři místa musí říkat totéž a hlídá to `scripts/verify-version.ps1`:

1. `VersionPrefix` v `Directory.Build.props` — jen `MAJOR.MINOR.PATCH`, bez přípony;
2. tag vydání — `v` a tatáž verze, volitelně s předvydáním;
3. sekce v `CHANGELOG.md` — `## [verze] - RRRR-MM-DD`.

Skript kontroluje i formát changelogu: první sekce musí být `[Nevydáno]`, verze musí být
platné podle specifikace, mít datum, být unikátní a jít v sestupném pořadí podle precedence.
Běží v CI při každém pushi a znovu před každým vydáním, takže rozpor se pozná při commitu,
ne až po pushnutí tagu.

### 14.2 Vydání nové verze

Vydání spouští tag. Postup je závazný a jeho první tři kroky vynucuje CI:

```powershell
# 1. zvýšit VersionPrefix v Directory.Build.props podle pravidel v 14.1
# 2. v CHANGELOG.md přepsat sekci [Nevydáno] na "## [0.9.0] - RRRR-MM-DD"
#    a založit novou prázdnou [Nevydáno]; doplnit i odkaz na porovnání na konci souboru
# 3. ověřit lokálně, ať se chyba nepozná až po pushnutí tagu
.\scripts\verify-version.ps1 -Tag v0.9.0

# 4. commitnout, pushnout main, otagovat a pushnout tag
git tag v0.9.0
git push origin v0.9.0
```

Workflow `.github/workflows/release.yml` nejdřív spustí tutéž kontrolu. Když tag,
`VersionPrefix` a changelog nesedí, skončí to chybou **před** buildem a publikací, takže
nevznikne poloviční vydání. Ze sekce changelogu se zároveň vytáhne popis vydání, který se
na GitHub pošle jako `body_path` — vydání tedy nemůže existovat bez záznamu v changelogu.
Neplatný tag je chyba; dřív se z něj potichu odvodila verze `0.0.0` a vydání se přesto
vypublikovalo.

Potom se sestaví Release, projdou testy i `dotnet format`, vypublikuje se jednosouborový
`.exe` a připojí se i s `.sha256`. Číslo v tagu se předává buildu přes `-p:Version=`, takže
se verze v aplikaci nemůže rozejít s číslem vydání.

`release.yml` se schválně nedá spustit přes `workflow_dispatch`: mimo tag by `github.ref_name`
nenesl verzi a vzniklo by vydání s nesmyslným číslem.

`.github/workflows/ci.yml` dělá totéž bez publikace při každém pushi do `main`.

### 14.3 Jak se aktualizace dostane k uživateli

Aplikace se po startu zeptá GitHub API na nejnovější vydání veřejného repozitáře. Veřejné
repo nevyžaduje přihlášení, takže aplikace nikde nedrží token.

Když je vydání novější než běžící verze:

1. `UpdateService` stáhne přílohu `BattlegroundsTracker.exe` vedle běžícího programu jako
   `BattlegroundsTracker.exe.new` a ověří ji proti `.sha256` z téhož vydání;
2. overlay ukáže zelený pruh, že je verze připravená;
3. `UpdateInstaller.Apply` při dalším startu odsune běžící `.exe` na `.old` a novou verzi
   přesune na jeho místo. Windows umí přejmenovat i běžící program, takže není potřeba
   žádný pomocný instalátor.

Instalace se záměrně odkládá na další spuštění, aby aktualizace nikdy nepřerušila
rozehraný zápas. Tlačítko **Restartovat** v pruhu výměnu provede hned a program spustí
znovu.

Selhání je vždy tiché a bez následků: nedostupný GitHub, nesedící kontrolní součet nebo
chybějící právo zápisu jen znamenají, že se nic nenainstaluje. Pokud se výměna nepovede
v půli, původní `.exe` se vrátí zpět na své místo.

Program proto musí ležet ve složce, kam má uživatel právo zápisu. V `Program Files` se
aktualizace neprovede.

### 14.4 Co distribuce zatím neřeší

`.exe` není podepsaný certifikátem, takže při prvním spuštění staženého souboru ukáže
Windows SmartScreen varování „Windows protected your PC“. Uživatel musí kliknout na
**More info** a **Run anyway**. Certifikát pro podepisování kódu je placený a pro nástroj
pro jednoho kamaráda se nevyplatí.

## 15. Automatické testy a dosavadní validace

Test suite aktuálně obsahuje padesát čtyři testů.

Původní čtyři:

1. Parsování `TAG_CHANGE` s entity descriptorem.
2. Redukci základního průběhu hry: Battlegrounds signál, kolo, fáze, HP po damage a
   Tavern Tier.
3. Odhalení entity, vazbu BattleTagu přes `HERO_ENTITY`, lobby hráče a zachování
   výsledku lokálního hráče.
4. Vytvoření zápasového archivu, zápis checkpointu, obnovení nedokončené hry, pokračování
   ve stejném souboru a uzavření bez založení duplicitního archivu.

Sedm nových v `BattlegroundsStateTests` je postavených na tvarech řádků odpozorovaných
ve skutečném osmihráčovém logu:

5. Kolo, zlato a cena upgradu tavernu včetně toho, že tag `TURN` na entitě hráče nesmí
   přepsat herní kolo.
6. Oddělení vlastní desky od nabídky Boba a vyřazení karty, jejíž descriptor pořád tvrdí
   `zone=PLAY`.
7. Pořadí v žebříčku, počet triplů a rozpoznání vyřazeného hráče.
8. Přednost skutečného BattleTagu z opožděné fronty před jménem Bobova skinu.
9. Výsledky soubojů, ignorování nulového resetu tagu poškození a konečné umístění.
10. Doplnění jmen karet z nabídky z výpisu `DebugPrintOptions`.
11. Ignorování statistik ze soubojové kopie hrdiny a pořadí podle zbývajících životů.
12. Založení nové hry, když té předchozí chybí `FINAL_GAMEOVER`.
13. Zachování lobby při opakované game konstrukci před prvním kolem.
14. `MatchRecorder` nad skutečným `MatchLogArchive`: dva zápasy skončí ve dvou souborech
    i bez `FINAL_GAMEOVER` u prvního z nich.
15. Sběr dostupných typů minionů jen z poolu, v obou pořadích tagů, s vyloučením Amalgáma
    i tokenů mimo pool.
16. Zapamatování soupeřovy desky ze souboje včetně dopsání jména, které dorazí až potom.

Tři v `UpdateTests` pokrývají aktualizace: ignorování vydání, které není novější, stažení
jen při sedícím kontrolním součtu a nasazení připravené verze se zachováním té předchozí.

Tři v `CardArtTests` pokrývají mezipaměť kreseb:

20. Stažení kresby jednou a další obsloužení z disku, i po vytvoření nového poskytovatele,
    a to bez zbytků `*.part`.
21. Zapamatování odpovědi 404, aby se na kartu bez kresby aplikace neptala znovu.
22. Opakování pokusu po výpadku sítě a odmítnutí Card ID, které by se nemělo dostat do
    cesty na disku ani do adresy.

Devět v `CardTextTests` pokrývá popisy karet: šest případů úklidu značek pro sazbu, výběr
jen battlegroundských karet s textem, čtení uložené kopie místo dalšího stahování a použití
i prošlé kopie, když se stažení nepovede.

Sedm v `DuosTests` pokrývá režim Duos: rozpoznání a spárování dvojic z tagů, řazení týmů se
zachováním dvojic pohromadě, pravidlo jeden BattleTag = jeden slot, pojmenování obou soupeřů
v hlášce o souboji, ohlášení týmu až s druhým vyřazeným hrdinou a udržení identity
lokálního hráče na jeho slotu i tehdy, když spoluhráč bojuje první, a to, že se jedno jméno
nikdy neobjeví na dvou řádcích tabulky zároveň.

Dva v `MatchLogArchiveTests` pokrývají kompresi: zabalení dohraného zápasu se zachováním
obsahu i řádovým zmenšením a dobalení pozůstalých prostých logů s ořezem retence.

Čtrnáct v `StatFormatTests` pokrývá zkrácený zápis statistik: hranice tisíce, desetinu jen do
deseti tisíc, zaokrouhlování vždy dolů a zástupný znak u neznámé hodnoty.

Poslední ověřený build prošel s 0 warnings a 0 errors; všech 54 testů prošlo.
`dotnet format --verify-no-changes` je bez nálezu.

Vedle jednotkových testů proběhlo živé ověření na reálné Battlegrounds hře:

- nalezení všech osmi hrdinů lobby;
- správné oddělení lokálního hráče od Bartendera;
- doplnění dostupných BattleTagů;
- aktualizace HP, armoru a tierů během hry;
- restart overlaye během aktivní hry;
- potvrzení, že po restartu zůstal stejný `ActiveMatchFile`, nevznikl další soubor a
  pokračovalo se od checkpointu;
- kontrola WPF accessibility stromu, že všech osm lobby položek je plně viditelných po
  čistém startu;
- kontrola výsledné výšky okna a obou seznamů.

Nová verze byla navíc ověřena přehráním celého archivu jedné skutečné třináctikolové hry
(343 797 řádků, 45 MB) přes `--replay`:

- všech osm BattleTagů včetně cyrilice bylo doplněno správně;
- deska měla přesně sedm minionů a nabídka Boba pět, bez zbytků z minulých kol;
- žebříček odpovídal zbývajícím životům a všichni vyřazení hráči byli rozpoznaní;
- historie soubojů dala 8 + 8 + 10 + 24 poškození, což přesně vyčerpalo 30 HP a 10 armoru;
- konečné umístění vyšlo na 2. místo, což odpovídá skutečnosti;
- celý replay trvá přibližně tři sekundy.

Overlay byl proti témuž logu spuštěn a zkontrolován snímkem obrazovky, včetně ověření,
že kolečko myši v panelu událostí skutečně scrolluje.

## 16. Známá omezení a technický dluh

1. **Nestabilní vstupní formát.** `Power.log` se může po patchi změnit bez upozornění.
2. **MMR není implementované.** V pozorovaném logu zatím není potvrzený zdroj.
3. **BattleTagy mohou být skryté.** Tracker zobrazuje jen data skutečně odhalená logem.
4. **Exkluzivní fullscreen.** Topmost WPF overlay nemusí být nad exkluzivním
   fullscreenem; doporučený je borderless fullscreen.
5. **Demo vs. live auto-detekce.** Aktivní Hearthstone může automaticky přepnout demo
   zpět do live režimu.
6. **Retence je jen podle počtu.** Drží se posledních pět zápasů bez ohledu na to, kolik
   místa zaberou. Velmi dlouhé zápasy tak můžou složku nafouknout víc, než by strop podle
   velikosti dovolil.
7. **Jeden globální checkpoint.** `checkpoint.json` reprezentuje naposledy použitý
   zdroj. Ruční střídání více nezávislých `Power.log` cest nemá samostatné checkpointy.
8. **Vybrat log u cizího souboru pořád archivuje.** Vlastní zápas z `matches` se pozná podle
   přípony a jen přehraje, ale otevření cizího `Power_old.log` z něj udělá živý zdroj se vším,
   co k tomu patří: založí archiv a přepíše checkpoint.
9. **První import může být pomalý.** Bez validního checkpointu se aktuální zdroj musí
   jednou přečíst celý.
10. **Konzolová větev zaostává.** Nemá match archivy, checkpoint ani přesné desktopové
   lobby zobrazení.
11. **Jména karet přicházejí opožděně.** Miniony soupeře hra vytvoří jen s Card ID.
    Tracker jméno doplní z dřív viděné stejné karty, jinak se pár sekund ukazuje
    `entity #id`, než dorazí opožděná fronta.
12. **Chybí databáze karet a lokalizace Card ID.** Zobrazuje se odhalené jméno z logu.
13. **Rozlišení nabídky a soupeřovy desky závisí na jediném tagu.** Obojí má stejný
    `CONTROLLER`; kdyby hra přestala psát `BACON_IN_COMBAT_PHASE`, zobrazí se špatný
    ze dvou seznamů.
14. **Generace desky se odvozuje od přepnutí fáze souboje.** Kdyby hra přegenerovala
    entity mimo toto přepnutí, deska by dočasně obsahovala zbytky z minulého kola.
15. **Cizí deska je vždy stará.** Log ji ukáže jen během souboje proti ní, takže podokno
    zobrazuje sestavu z posledního vzájemného souboje, ne aktuální stav. Proti komu jste
    ještě nenastoupili, jeho desku nelze zjistit vůbec.
16. **Typy minionů jsou pozorované, ne oficiální.** Seznam se plní za běhu, takže je
    v prvních kolech neúplný, a karta patřící do víc poolů zároveň může přidat typ, který
    lobby ve skutečnosti nenabízí. Podrobnosti v kapitole 8.6.
17. **Pozice a šířka okna se nepersistují.** Výška se vypočítá při startu, uživatelské
    přesunutí nebo resize se mezi relacemi neukládají.
18. **Sbalení desek se nepamatuje.** Na nižším monitoru se sekce sbalí sama při každém
    startu, ruční volba se ale mezi spuštěními neukládá.
19. **Jména spoluhráčů v Duos nejsou vždy k mání.** Log pojmenuje jen hráče, jejichž entita
    se v něm objevila; ostatní zůstanou jako `Skrytý hráč`. Ve zkoumaném zápase šlo o dva
    sloty z osmi. Deska spoluhráče v logu není vůbec, podokno u něj proto zůstane prázdné.
20. **Soukromí logů.** Vlastní archivy mohou obsahovat BattleTagy a další syrová data;
    nemají se automaticky sdílet bez kontroly.
21. **Kresby a popisy karet potřebují internet.** Aplikace kvůli nim chodí na cizí servery
    (`art.hearthstonejson.com` a `api.hearthstonejson.com`). Neposílá o uživateli nic než
    Card ID a `User-Agent`, ale je to druhé místo po kontrole aktualizací, kde tracker sahá
    ven. Bez sítě a bez naplněné mezipaměti se kartičky vykreslí jen s údaji z logu.
22. **Kresba nebo popis nemusí existovat.** Zdroj je nezávislý na Blizzardu a u čerstvě
    vydaných karet může zaostávat. Kartička to snese, jen bude bez obrázku nebo bez popisu.
    Popisy jsou navíc jen anglicky; databáze má i jiné jazyky, ale tracker si bere `enUS`.
23. **Popis je z databáze, ne z rozehrané partie.** Ukazuje text vytištěný na kartě.
    Nezohledňuje, co s minionem udělaly buffy nebo trinkety; skutečné jsou jen statistiky
    a klíčová slova, které tracker čte z logu.
24. **Dekódované obrázky se z paměti neuvolňují.** `CardCache` je drží po celý běh
    aplikace. Jedna kresba zabere zhruba 260 kB, takže dlouhá relace s mnoha různými
    kartami paměť pozvolna zvedá.

## 17. Doporučené další kroky

Doporučené pořadí dalšího vývoje:

1. Přidat integrační fixture z anonymizovaného reálného osmihračového logu a otestovat
   všechny možné pořadí `FULL_ENTITY`, tagů a `HERO_ENTITY`.
2. Oddělit uživatelsky vynucené demo od automatické live detekce.
3. Přidat gzip kompresi dokončených logů a konfigurovatelnou retenci.
4. Sjednotit konzolovou i desktopovou aplikaci na `PowerLogDiscovery`,
   `PowerLogTailReader` a případně `MatchLogArchive`.
5. Ukládat checkpointy per zdroj, například podle hashe absolutní cesty.
6. Přidat diagnostický panel nebo export anonymizovaného výřezu při neznámém formátu.
7. Prověřit, zda lze MMR získat spolehlivě a v souladu s pravidly z jiného lokálního
   logu; do té doby sloupec nezobrazovat.
8. Persistovat umístění, šířku a uživatelskou preferenci výšky overlaye s bezpečným
   omezením na aktuální pracovní plochu monitoru.
9. Přidat kompaktní režim rozložení pro monitory nižší než 1084 px.
10. Ukládat po konci hry strukturované shrnutí zápasu, aby šlo stavět dlouhodobé
    statistiky bez opětovného parsování raw logů.
11. Doplnit databázi karet a lokalizaci Card ID, aby jména nezávisela na tom, co log
    zrovna odhalil.
12. Rozšířit model o trinkety, hero power a Dark Gifts. Tagy `BACON_FIRST_TRINKET_DATABASE_ID`,
    `BACON_SECOND_TRINKET_DATABASE_ID`, `HERO_POWER_ENTITY` a `BACON_DARK_GIFTS_ACTIVE`
    v logu jsou, ale bez databáze karet z nich jsou jen číselná ID.

## 18. Troubleshooting

### Aplikace zůstává v režimu NASLOUCHÁM

- ověřit, že běží proces `Hearthstone`;
- ověřit konfiguraci `log.config`;
- najít nejnovější session adresář `Hearthstone_*` a zkontrolovat, že `Power.log` roste;
- použít **Vybrat log** nebo argument `--log`;
- není nutné spouštět aplikaci jako správce.

### Lobby ukazuje méně než osm hráčů

- zkontrolovat, zda hra už odhalila hero entity a jejich `PLAYER_ID`;
- hledat u každé hero entity `CARDTYPE=HERO`, `PLAYER_ID` a odhalené jméno;
- nevyvozovat lobby slot z `CONTROLLER`;
- BattleTag může legitimně zůstat skrytý, hrdina by se ale po dostupnosti entity měl
  zobrazit;
- po patchi porovnat řádky s regexy v `PowerLogParser`.

### Po restartu se hra neobnoví

- zkontrolovat `%LOCALAPPDATA%\BattlegroundsTracker\checkpoint.json`;
- ověřit shodu `SourcePath` s aktuálně čteným logem;
- ověřit existenci `ActiveMatchFile`;
- pokud byl zdroj zkrácen a `SourcePosition` je za jeho koncem, checkpoint se záměrně
  ignoruje;
- poškozený checkpoint lze po zavření aplikace přesunout stranou; další start provede
  jednorázový plný replay.

### Overlay není nad hrou

- přepnout Hearthstone z exclusive fullscreen na borderless/windowed fullscreen;
- ověřit, že okno není sbalené na horní lištu;
- overlay má `Topmost=true`, ale nemůže garantovat zobrazení nad exkluzivním DirectX
  fullscreen surface.

### Logy zabírají mnoho místa

- bezpečně lze po ukončení trackeru archivovat nebo odstranit staré dokončené soubory v
  `%LOCALAPPDATA%\BattlegroundsTracker\matches`;
- nemaže se aktivní soubor uvedený v `checkpoint.json`;
- nemaže ani nezkracuje se aktivní Blizzard `Power.log`;
- automatická retence zatím není implementovaná.

## 19. Shrnutí důležitých návrhových rozhodnutí

- Zdroj hry se pouze čte; vlastní historie je oddělená.
- Rychlý restart řeší byte checkpoint plus replay jediného nedokončeného zápasu.
- Lobby identita je `hero PLAYER_ID`, nikoli `CONTROLLER`.
- BattleTag se připojuje přes `HERO_ENTITY -> heroEntityId`.
- Neúplné nebo nedostupné údaje se zobrazují jako `—`/`Skrytý hráč`, nevymýšlejí se.
- Duplicitní `CREATE_GAME` během aktivní hry nesmí resetovat stav.
- Konec zápasu je `STEP=FINAL_GAMEOVER`.
- Stav hry se bere z `GameState.*`; `PowerTaskList.*` slouží jen k doplnění jmen.
- Zóna a pozice se čtou z tagů, nikdy z entity descriptoru.
- Deska se filtruje podle epochy, protože Battlegrounds entity každé kolo přegeneruje.
- Statistiky hráče drží leaderboard hrdina, ne jeho soubojová kopie.
- Pořadí v lobby se počítá ze zbývajících životů, ne z tagu, který v logu zaostává.
- Cizí deska se zachytí při prvním útoku souboje a v UI se vždy ukazuje s číslem kola,
  protože novější stav log nenabízí.
- Typy minionů se sbírají z toho, co Bob skutečně nabídl, ne z tagů `BACON_SUBSET_*`.
- Desktopová tabulka používá přesný lobby model, ne obecný seznam všech entit.
- UI je výchozím stavem dostatečně vysoké pro osm hráčů i osm posledních událostí a
  přitom zůstává ručně přesouvatelné, sbalitelné a ukončitelné.
