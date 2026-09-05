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
- WPF (`net8.0-windows10.0.19041.0`) pro desktopový overlay; verze Windows v cíli otevírá
  WinRT rozhraní, ze kterého čte proužek s hudbou, a stanoví minimum Windows 10 verze 2004;
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
| `src/Tracker.Core` | Parser `Power.log`, hledání instalace hry, geometrie okna, redukce událostí do stavu, model lobby, objevování logu, tail reader, archivace zápasů, `MatchRecorder`, uživatelské nastavení (`TrackerSettings`, `SettingsStore`), složka dat (`AppPaths`), data proužku s hudbou a stahování kreseb a popisů karet. |
| `src/Tracker.Desktop` | Hlavní WPF overlay, režimy naslouchání/demo/live, view model, design systém (`Themes/Controls.xaml`, `ThemeManager`), okno nastavení, ovládání okna, čtení systémových relací médií a okno s patch notes ve vestavěném prohlížeči. |
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
log a nakonec režim naslouchání. Od 0.10.1 se explicitní log čte **živě jen tehdy, když
patří běžící hře** (`IsCurrentSessionLog`, tedy poslední zápis je novější než start procesu
Hearthstone mínus minuta); jinak se přehraje. Bez toho zakládal každý spuštěný `--log` nový
záznam v archivu zápasů a přepsal checkpoint, takže retence na pěti zápasech vytlačila
skutečně odehrané hry. Přehrávání do `%LOCALAPPDATA%` nezapisuje vůbec nic.
V současné implementaci má automaticky zjištěná živá
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
`PROPOSED_ATTACKER` po příchodu hrdiny do souboje: v tu chvíli je jeho deska postavená
a ještě nikdo nezemřel, takže jde přesně o sestavu, se kterou nastoupil. Souboj, ve kterém
nikdo nezaútočil, se zachytí až při jeho ukončení, a to jen na straně, kde se hrdina
nestřídal.

Komu deska patří, říká `HERO_ENTITY` entity hráče na dané straně: v sólu je na soupeřově
straně jediná soubojová kopie hrdiny, v Duos se tam během souboje vystřídají oba hrdinové
dvojice a na lokální straně může stát spoluhráč (kapitola 8.8). Když `HERO_ENTITY` slot
neprozradí, bere se soubojová kopie hrdiny na soupeřově straně; ta je spolehlivější než
`NEXT_OPPONENT_PLAYER_ID`, který se v logu mění dřív, než souboj doopravdy začne.

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

#### Útok undeadů je jinde

Pátý bonus téhle skupiny, „your Undead have +X Attack this game“, se **na entitě hráče
nevyskytuje**. V enumu hry tag `UNDEAD_ATTACK_BUFF` existuje (najde se v
`Hearthstone_Data/Managed/Assembly-CSharp.dll`), ale v žádném pozorovaném logu ani jednou
nepadl. Rodina `*BUFF*VALUE*` má v assembly jen blood gemy, elementály a piráty.

Hodnotu místo toho nese **enchantment na hráči**: `Undead Bonus Attack Player Enchant [DNT]`
s kartou `BG25_011pe`, v `TAG_SCRIPT_DATA_NUM_1`. Naměřeno na kartě Nerubian Deathswarmer
(`BG25_011`, „Battlecry: Your Undead have +1 Attack this game“): v jedné hře hodnota rostla
5 → 6 → 10 → 11 → 12 → … → 25 a v jiné vyšplhala na 255. Tentýž součet se zrcadlí i na
enchantmentech jednotlivých undeadů (`Undead Army`, `BG25_011e2`).

Každý hráč má vlastní takový enchantment, takže se filtruje na controller lokálního hráče,
a nula se zahazuje ze stejného důvodu jako u tagů: enchantment se každým soubojem
přegeneruje a ten odcházející dostane nulu, ačkoli bonus platí dál.

Life se u undeadů takhle nebuffuje, proto má tenhle bonus jen jednu hodnotu.

Stejný mechanismus mají i další „this game“ efekty, každý se svou kartou: `BG31_808pe`
pro beetly (+3/+2), `BG25_008pe` pro počet padlých Eternal Knightů, `BG35_152pe` pro buff
minionů v krčmě. Zapojený je zatím jen ten pro undeady, protože k ostatním nejsou naměřená
data.

V UI je řádek `Bonusy: …` pod výpisem typů a ukazuje jen to, co v dané hře skutečně
nastalo; když nic, řádek se schová a nebere místo. V bočním panelu má každý bonus vlastní
řádek (viz 11.3a).

### 8.7 Souboje a konečné umístění

Přepnutí `BACON_IN_COMBAT_PHASE` na 1 zakládá nový `CombatRound` s číslem kola a slotem
soupeře z `NEXT_OPPONENT_PLAYER_ID`. Výsledek se doplňuje až v následující nákupní fázi:

- `BACON_WON_LAST_COMBAT=1` znamená výhru;
- kladné `DAMAGE_DEALT_TO_HERO_LAST_TURN` znamená prohru a zapíše utrpěné poškození;
- souboj, který do začátku dalšího souboje výsledek nedostane, se uzavře jako remíza.

Konečné umístění se při `FINAL_GAMEOVER` bere z `PLAYER_LEADERBOARD_PLACE` lokálního hrdiny.
Konec hry se hlásí jednou: umístěním, a jen když ho log nedá, holým výsledkem.

Vyřazení se hlásí ve chvíli, kdy hráči dojdou životy. Tag `PLAYER_LEADERBOARD_PLACE` v tu chvíli
ještě nese živé pořadí z doby, kdy hráč žil (naměřeno: hráč vyřazený jako pátý měl dvojku),
a skutečné umístění se po vyřazení ještě několikrát přeskládá, než se usadí. Umístění se proto
počítá z toho, kolik hráčů, v Duos týmů, zůstalo ve hře; tag se použije jen tehdy, když lobby
není kompletní. Padnou-li dva hráči v jednom kole, rozhoduje mezi nimi zbývající počet životů:
kdo skončil blíž nule, je výš (naměřeno −1 před −14 v sólu a −3 před −4 v Duos). Pořadí tagů
`DAMAGE` to nemusí sledovat, takže se dotčené hlášky přepíšou na místě.

### 8.8 Režim Duos

Duos pozná tracker podle tagů `BACON_DUO_*` a přepne se do `TrackerState.IsDuos`. Osm hráčů
tvoří čtyři dvojice a rozdávají se čtyři místa, ne osm (`PlaceCount`).

| Tag | Kde | Význam |
| --- | --- | --- |
| `BACON_DUO_TEAM_ID` | entita hrdiny, u lokálního hráče entita hráče | tým 1 až 4 |
| `BACON_DUO_TEAMMATE_PLAYER_ID` | entita lokálního hráče | slot spoluhráče |
| `NEXT_OPPONENT_TEAMMATE_PLAYER_ID` | entita lokálního hráče | druhý ze soupeřící dvojice |
| `BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT` | entita hráče a všichni hrdinové lobby | kdo z dvojice bojuje v příštím souboji první |
| `BACON_DUO_PAIR_CANDIDATE_TEAMMATE`, `BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE` | karta v nabídce nebo v ruce | karta by spoluhráči složila pár či triple |
| `IS_USING_PASS_OPTION` | karta v ruce | lokální hráč ji právě předává spoluhráči |
| `BACON_DUO_PASSABLE` | karty v nabídce a v ruce | karta se dá předat; tracker ho nečte |
| `BACON_TEAMMATE_BONUS_MINION_DAMAGE_LAST_COMBAT` | soupeřova entita hráče | příznak výpočtu poškození; tracker ho nečte |
| `BACON_DUOS_PUNISH_LEAVERS` | `GameEntity` | trest za odchod ze hry; na konci hry padne na nulu |

Dvojice **nejsou sousední sloty**. V pozorovaném logu tvořily tým sloty 3 a 8, takže se
partner bez tagu odvodit nedá. Vlastní tým navíc hra napíše jinam než u ostatních: na entitu
hráče, ne na entitu hrdiny. `LinkLocalTeam` proto číslo doplní lokálnímu hráči i jeho
spoluhráči, ať přišlo z kterékoli strany.

**Souboj je týmový a sekvenční.** Nastupuje ten z dvojice, koho označuje
`BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT`, proti tomu ze soupeřů, koho nese
`NEXT_OPPONENT_PLAYER_ID`; v pěti měřených zápasech šlo vždy o soupeře s týmž příznakem.
Jakmile padne některá z desek, přijde na tutéž stranu druhý hrdina té dvojice se svou deskou
a bojuje proti tomu, co na druhé straně zbylo. V logu se `HERO_ENTITY` entity hráče přepne na
soubojovou kopii druhého hrdiny a hra jeho miniony postaví na stejný `CONTROLLER`; podrobnosti
v kapitole 8.9. Hláška o souboji proto jmenuje oba soupeře:
`Kolo 3 · tým vs Overlord Saurfang + Snake Eyes: prohra, dostali jsme 4 dmg.` Řádek s dalším
soupeřem jmenuje oba hrdiny dvojice v pořadí, v jakém nastoupí, a říká, kdo začíná za náš tým.

Desky všech čtyř účastníků se ukládají po jednom: pro každou stranu desky tracker sleduje, čí
hrdina na ní právě stojí, a jeho desku uloží při prvním útoku po jeho příchodu. Deska
spoluhráče se tak objeví v podokně jeho řádku s číslem kola stejně jako desky obou soupeřů.
Kdo přišel do souboje s prázdnou deskou, žádnou uloženou nemá.

**Dvojice sdílí životy.** Hra píše `HEALTH`, `ARMOR` i `DAMAGE` na oba hrdiny se stejnou
hodnotou, ale ne ve stejném okamžiku; naměřeno až 4 400 řádků rozestupu. Tracker proto hodnoty
mezi spoluhráči zrcadlí, a to i do entity hrdiny v lobby, aby ho každá další zmínka v logu
nevracela na starou hodnotu. Tým tak padá naráz a hlásí se jednou:
`Tým Buttons + Tras'tath, Soul Parasite vypadl na 2. místě.` Řazení týmů v žebříčku bere vyšší
z obou hodnot zbývajících životů, ne součet, který by životy počítal dvakrát.

**Poškození dorazí dvakrát.** Když lokální hráč bojoval první a tým prohrál, zapíše hra
`DAMAGE_DEALT_TO_HERO_LAST_TURN` dvakrát: jednou za soubojovou kopii spoluhráče, která souboj
dobojovávala, a hned potom za vlastního hrdinu, takže druhá hodnota je dvojnásobek (4→8,
12→24, 15→30, 9→18, 7→14). Skutečný úbytek životů týmu odpovídal ve všech pěti měřených
zápasech první hodnotě, proto se v Duos bere první nenulový zápis a další v témže souboji se
zahazují. Když bojoval první spoluhráč, přijde hodnota jen jednou. V sólu se nic nemění.

Hlášky o souboji nesou číslo kola. Výsledek dorazí až v další nákupní fázi a u remízy dokonce
až se začátkem dalšího souboje, takže by jinak visel pod nadpisem cizího kola.

`PLAYER_LEADERBOARD_PLACE` nese v Duos umístění **týmu**, tedy 1 až 4, a oba spoluhráči mají
stejné. Řadit hráče jednotlivě by dvojice roztrhalo, proto `TrackerState.Teams` seskupí lobby
podle `TeamId`, týmy seřadí podle zbývajících životů a uvnitř týmu dá dopředu lokálního
hráče. Číslo místa se pak píše jen k prvnímu z dvojice. Umístění vyřazeného týmu se počítá
z počtu týmů, které zůstaly ve hře (kapitola 8.7); tag by v tu chvíli dal živé pořadí.

Předání karty spoluhráči se pozná podle `IS_USING_PASS_OPTION` na kartě v ruce: karta odejde do
`SETASIDE` a u spoluhráče vznikne kopie, kterou už log neukáže. Tracker to ohlásí
`Předal jsem spoluhráči: Proud Privateer.` Opačný směr, tedy karta od spoluhráče, v logu
vlastní stopu nemá; v ruce se objeví jako každá jiná karta. Karty, které by spoluhráči složily
pár nebo triple, hra značí tagy `BACON_DUO_PAIR_CANDIDATE_TEAMMATE` a
`BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE`; tracker je u minionů v nabídce vypisuje jako
`pár pro spoluhráče`, respektive `triple pro spoluhráče`.

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

Deska spoluhráče se mimo souboj v logu neobjeví: v nákupní fázi je na straně lokálního hráče
vždycky nanejvýš sedm minionů, tedy jen vlastní deska. V souboji ale hra spoluhráčovy miniony
postaví na tutéž stranu (`CONTROLLER`) jako ty vlastní, jakmile se do boje přidá, a tak se dá
jeho deska uložit stejně jako soupeřova. Během souboje se proto vlastní deska nadepisuje podle
toho, čí hrdina na lokální straně právě stojí: `MOJE DESKA`, nebo `DESKA SPOLUHRÁČE · Drek'Thar`.
Na soupeřově straně se nadpis doplní o jméno hrdiny, který tam právě je.

Kromě soubojových kopií, které bojují, vyrobí hra na začátku každého souboje ještě referenční
kopie ostatních tří hrdinů i s jejich miniony v `SETASIDE` na lokální straně. Nesou
`BACON_COMBAT_PHASE_HERO`, takže do lobby nezasahují, a do desek nevstupují, protože nejsou
v `PLAY`.

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
z UI odstraněn a nahrazen počtem triplů.

Prohledané byly 5. 9. 2026 i ostatní logy hry z adresáře relace (`Hearthstone.log`,
`GameNetLogger.log`, `LoadingScreen.log`, `Achievements.log`, `Gameplay.log`, `Asset.log`,
`LuckyDraw.log`) a Unity `Player.log` v `%USERPROFILE%\AppData\LocalLow\Blizzard
Entertainment\Hearthstone`. Jediné zásahy na `rating` jsou chybové hlášky o DBF; rating
Battlegrounds hra do žádného logu nepíše. Jiné trackery ho čtou z paměti procesu hry
(HearthMirror), což tento tracker záměrně nedělá. MMR proto doplňuje uživatel v historii
zápasů (11.3c) a tracker z něj počítá změny a aktuální zůstatek.

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

### 9.14 Slot na trinket je jedna entita, která se přepíše

Každý hráč má dvě entity slotů na trinket a ty žijí celou hru. Dokud je slot prázdný, jmenuje
se `Lesser Trinket` nebo `Greater Trinket`, má kartu `BG30_Trinket_1st` / `BG30_Trinket_2nd`
a nese odpočet `BACON_TURNS_LEFT_TO_DISCOVER_TRINKET`. **Po výběru se tatáž entita přepíše na
vybraný trinket** řádkem `CHANGE_ENTITY`, takže slot se pozná jen podle karty, se kterou
entita vznikla; proto se označení slotu ukládá při prvním přiřazení karty a už se nemění.

Dvě věci k tomu bylo nutné naměřit:

- **Prázdných slotů vyrobí hra za zápas desítky.** V jedné session jich bylo pro dva hráče
  sedmnáct, všechny s odpočtem ze začátku hry a v zóně `REMOVEDFROMGAME`. Platí jen ten jeden
  v `PLAY`; bez filtru na zónu ukazoval panel odpočet i dlouho po tom, co byl trinket vybraný.
- **Jméno a karta se nemění zároveň.** Jméno se obnoví z kteréhokoli descriptoru, kdežto karta
  až tehdy, když entitu zmíní řádek s novým `cardId`. Naměřeno na zápase, kde první slot měl
  po výběru obojí (`Lovely Locket`, `BG36_MagicItem_211`), ale druhý jen jméno
  (`Beatboxer Portrait` s pořád ještě kartou `BG30_Trinket_2nd`). Obsazenost slotu se proto
  pozná po jménu, které je zároveň to, co se v panelu vypisuje.

### 9.15 Ekonomika krčmy sedí na tlačítkách, ne na hráči

Cena rerollu (`COST`) i počet volných rerollů (`BACON_FREE_REFRESH_COUNT`) jsou na tlačítku
`TB_BaconShop_8p_Reroll_Button` daného hráče, cena upgradu na tlačítkách
`TB_BaconShopTechUpNN_Button`. Volné rerolly hra píše i na pomocnou enchantment entitu
`Bacon_Free_Refresh_Player_Ench`, ale stav, který uživatel vidí, drží tlačítko: naměřený průběh
2 → 4 → 3 → 2 → 1 odpovídá dvěma přírůstkům a postupnému utrácení.

Zlato navíc na příští kolo (`BACON_PLAYER_EXTRA_GOLD_NEXT_TURN`) je naopak na entitě hráče
jako bonusy pro celou hru, ale **nesmí se na něj použít jejich pravidlo o zahazování nuly**:
po utracení se skutečně vrací na nulu, kdežto bonusy se vracejí na starou hodnotu.

Strop tavern tieru (`BACON_MAX_PLAYER_TECH_LEVEL`, hodnota 6) je na entitách hrdinů a platí
pro celou lobby. Bez něj nejde poznat, jestli cena upgradu chybí, nebo jestli je hráč na
posledním tieru, kde ji hra přestane posílat.

`BACON_SELL_VALUE` je proti tomu na každém minionovi zvlášť, takže nejde o ekonomiku hráče,
ale o cenu prodeje jedné karty.

Zlato navíc se v kole, kdy se uplatní, objeví jako `TEMP_RESOURCES`. Proto může být k utracení
víc, než kolik kolo dává, a proto má panel řádek „z toho navíc“ — bez něj vypadá poměr
`20/11` jako chyba.

### 9.16 Bonus, který karta drží sama

Ne každý bonus platný pro celou hru je na entitě hráče. Na entitě jsou jen čtyři: tavern kouzla,
blood gemy, elementálové a piráti (viz 8.6b). Karty typu „**and improve this**“ si hodnotu, kterou
právě dávají, drží samy v `TAG_SCRIPT_DATA_NUM_1` a `_2`.

Naměřeno na kartě **Spark Snapper** (`BG36_851`, „Whenever you play a Mech, Magnetize a 2/2
Satellite to it and improve this“): text karty v databázi pořád tvrdí 2/2, ale počítadla během
zápasu vyrostla na 26 a 28. Bez nich tedy skutečnou hodnotu satelitů zjistit nelze.

**Co ta dvě čísla znamenají**, se muselo změřit, protože nesouměrné 26/28 nedává smysl. Obě
rostou po dvou a `NUM_2` je vždycky `NUM_1 + 2`, tedy základ karty. Rozhodl skok statistik na
desce: ve chvíli, kdy `NUM_1` = 26 a `NUM_2` = 28, dostal `Polarizing Beatboxer` +28 útoku
(619 → 647) a +28 života (673 → 701). **`NUM_1` je tedy nasčítaný přírůstek nad základ 2/2
a `NUM_2` je výsledná hodnota**, kterou karta právě dává. Panel proto z obou počítadel vypisuje
to vyšší jako jedno číslo; `26/28` by tvrdilo, že je satelit nesouměrný, což není.

Počítadla se navíc při přechodu do souboje nulují, ale **na odcházející entitě**: hodnotu 2, 4, 6
nesla entita 11386, pak dostala nulu a nová entita 13719 pokračovala od 8. Deska se filtruje
podle generace, takže se do panelu dostane jen ta nová se správnou hodnotou.

Ostatní karty používají tatáž počítadla k vnitřní evidenci: `Scrap Scraper` má
`TAG_SCRIPT_DATA_NUM_1` = 2 a jeho text („Deathrattle: Get a random Magnetic Mech“) žádné číslo
nemá. Panel proto vypisuje jen karty, jejichž text obsahuje „improve this“ — u nich se hodnota
z počítadla od čísel v textu doopravdy liší. Filtr je na anglickém textu z databáze karet, takže
jinak formulovanou rostoucí kartu mine; radši nic než nesrozumitelné číslo.

Text se čte z `CardTextProvider.Loaded`, tedy ze synchronního náhledu na už načtenou tabulku.
Přes `CardInfo` to nešlo: ten se doplňuje asynchronně, takže v okamžiku, kdy karta na desce
přibude, je jeho popis ještě prázdný a sekce by se objevila až o jeden přepočet později — a při
přehrávání hotového logu, kde po posledním řádku žádný další přepočet nepřijde, vůbec.

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
  settings.json                              (uživatelské nastavení, viz 11.7)
  history.json                               (historie zápasů s MMR, viz 11.3c)
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

Celou složku přesměruje proměnná prostředí `BGTRACKER_DATA_DIR` (`AppPaths.DataDirectory`).
Hodí se pro přenosnou instalaci a pro snímky rozhraní nad čistými daty; proměnnou
`LOCALAPPDATA` .NET na Windows nečte a bere složku přímo ze systému.

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

Drží se posledních `RetainedMatches` dohraných zápasů; výchozí je pět
(`MatchLogArchive.DefaultRetainedMatches`), v nastavení jde zvolit 1 až 200. Ořez i dobalení
pozůstalých prostých logů proběhnou při otevření archivu, takže se složka srovná i po
aktualizaci ze starší verze nebo po pádu aplikace; snížení retence za běhu ořeže složku hned.
Historie zápasů (`history.json`) na retenci nezávisí, takže záznam zápasu zůstane, i když jeho
log už není k přehrání.

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

Overlay je bezrámové WPF okno s průhledným okolím, vlastním designem a `Topmost=true`, které
se dá v nastavení vypnout. Ikona okna i `.exe` je `assets/bg-tracker.ico`; v hlavičce je
stejná značka „BG“ vykreslená přímo v XAML. Hlavička nese název, verzi, režim čtení se
zdrojem, v Duos štítek `DUOS`, a čtyři ikonová tlačítka: nastavení, panel s detaily, sbalení
a zavření. Okno se táhne za hlavičku; ta je jediné místo, kterým se chytá, proto ji
`WindowPlacement` nikdy nepustí mimo monitory.

Dvojklik na hlavičku nebo tlačítko sbalení overlay sbalí na pruh hlavičky: obsah se skryje,
karta se přirozeně zmenší na výšku hlavičky a okno s ní, resize se vypne a hlavička se
zakulatí i dole, protože je pak celou kartou. Zvětšení zůstává stejné jako před sbalením,
aby pruh neposkočil. V nastavení jde zvolit, že overlay startuje sbalený.

Vzhled řídí design systém v `Themes/Controls.xaml`: tvary a chování ovládacích prvků jsou
v XAML, barvy plní za běhu `ThemeManager` do zdrojů aplikace pod klíči `Brush.*` podle
nastavení (tmavý nebo světlý základ, šest akcentů, krytí okna). XAML na ně odkazuje přes
`DynamicResource`, takže přepnutí motivu překreslí hlavní okno, nastavení i patch notes bez
restartu. Klíčové styly `TextBlock` musí stavět na implicitním stylu (`BasedOn`), jinak
nedostanou barvu textu z motivu a zůstanou černé; přesně tak dopadla první verze okna
nastavení. Písmo je Segoe UI Variable s návratem na Segoe UI.

Běžné topmost WPF okno funguje spolehlivě nad windowed nebo borderless fullscreenem.
Nad exkluzivním fullscreen režimem Windows nemusí overlay zobrazit. To je omezení typu
okna, nikoli parseru.

### 11.2 Velikost okna

Rozložení se sází v návrhových jednotkách: hlavní sloupec je široký 380, panel s detaily
vpravo přidává 214 a hlavička má 42 na výšku. Výška karty není pevná: sekce jsou nad sebou
ve `StackPanel` a karta má přirozenou výšku toho, co je právě vidět. Celý obsah je zabalený
do `Viewbox` se `Stretch="Uniform"` a okno se při každé změně velikosti karty
(`RootCard.SizeChanged`) nastaví na návrhovou velikost vynásobenou zvětšením. Overlay tak
zabere stejný podíl obrazovky na FullHD i na 4K, nic se neořezává ani nescrolluje, a když
uživatel sekci schová, okno se o ni zmenší.

Zvětšení určuje nastavení (60 až 160 %, výchozí 100 %) a volitelný strop „vejít se na
obrazovku“: zvětšení se stáhne tak, aby rozbalené okno nezabralo víc než zvolený podíl výšky
pracovní plochy (výchozí 85 %):

```text
zvětšení = clamp(min(nastavené, výška pracovní plochy × podíl / výška karty), 0,6, 1,6)
```

Pracovní plocha je v jednotkách nezávislých na DPI, takže se do výpočtu nezanese zvětšení,
které si Windows nastavují samy. Naměřeno s výchozím nastavením: 594 × 808 px s panelem
vpravo a 380 × 808 bez něj; s detaily dole, rukou a pohodlnou lobby vyjde karta na zhruba
1375 jednotek a strop ji na pracovní ploše 1392 stáhne na 327 × 1183.

Tažení za úchop v pravém dolním rohu mění zvětšení: po čtvrt sekundě klidu se z nové šířky,
nebo výšky podle toho, co uživatel táhl, spočítá zvětšení, zapíše do nastavení a okno dostane
přesně odpovídající druhý rozměr, aby `Viewbox` nenechal prázdné pruhy. Velikost, kterou
nastavil kód, se pozná porovnáním s očekávanou; `SizeChanged` totiž přichází až
s rozvržením, ne v okamžiku, kdy kód vlastnost nastaví.

Seznamy lobby, desky a nabídky jsou `ItemsControl`, nikoli `ListBox`, takže scrollovat
principiálně nemohou. Místo pro maximální počet položek si rezervují přes `MinHeight`: osm
řádků lobby, sedm minionů na desce i v nabídce, zvolený počet událostí. Okno tak při změně
počtu položek neposkakuje. Jediný scrollovatelný panel jsou události: mají strop na
dvojnásobek rezervované výšky, takže delší zalomené texty rolují, místo aby zvětšovaly okno.

Poloha okna se pamatuje a při startu obnoví, pokud to uživatel nevypne; `WindowPlacement.Clamp`
ji stáhne zpátky, když monitor, na kterém overlay skončil, už není.

### 11.3 Obsah

Pod hlavičkou jdou sekce nad sebou; každá se dá v nastavení vypnout a většina má v nadpisu
šipku, kterou se za běhu sbalí:

1. **Přehled** – čtyři dlaždice: kolo, místo v žebříčku (`5/8`, v Duos `2/4`), zlato a fáze.
2. **Lobby** – řádek s dalším soupeřem a cenou upgradu tavernu, typy minionů v nabídce
   a tabulka osmi hráčů se sloupci `#`, `HRDINA`, `HP`, `ARM`, `TIER`, `TRIP`. Kompaktní
   hustota má hrdinu i BattleTag na jednom řádku (22 jednotek), pohodlná na dvou (30);
   BattleTagy jdou schovat úplně.
3. **Desky** – vlastní deska a pod ní `NABÍDKA BOBA`, nebo v souboji `DESKA SOUPEŘE`.
4. **Ruka** – karty v ruce včetně tavern kouzel; ve výchozím stavu vypnutá, protože ji hra
   ukazuje sama.
5. **Detaily** – bonusy pro celou hru, bonusy na kartách, trinkety a ekonomika krčmy; buď ve
   sloupci vpravo, nebo pod hlavním sloupcem (11.3a).
6. **Události** – dvě až šest posledních událostí.
7. **Historie** – poslední zápasy zvoleného režimu s hrdinou, umístěním, změnou MMR
   a zůstatkem MMR, přepínač sólo/Duos a štítek s aktuálním MMR (11.3c).
8. Pruhy, které se ukazují jen občas: aktualizace, načítání zápasu a hudba (11.3b).
9. **Patička** – stav a výsledek, po najetí diagnostika parseru; vpravo patch notes a menu
   s ovládáním.

Stejné grid šířky používá hlavička tabulky i každý datový řádek, proto jsou hodnoty přesně
pod nadpisy. Název hrdiny a BattleTag se při nedostatku šířky oříznou ellipsis.

Řádek lobby je barevně odlišený: akcentem pro lokálního hráče, zeleně pro spoluhráče v Duos,
oranžově pro dalšího soupeře. V Duos jsou dvojice oddělené mezerou a číslo místa se píše jen
k prvnímu z nich, protože místo patří týmu; podrobnosti v kapitole 8.8. V hlavičce okna přibude
vedle režimu čtení štítek `DUOS`. Vyřazený hráč je ztlumený, má lebku před jménem hrdiny
a místo HP křížek.

Načítání uloženého zápasu běží na pozadí a nad tlačítky se po tu dobu ukazuje pruh s postupem.
Půl milionu řádků se parsuje pár sekund a na vlákně rozhraní by okno mezitím zamrzlo. Postup se
počítá z pozice v souboru na disku, protože počet řádků se u zabaleného zápasu předem zjistit
nedá. V hlavičce se místo jména souboru ukazuje jen datum a čas zápasu, celá cesta je
v podokně; jméno archivu má přes čtyřicet znaků a z hlavičky přetékalo.

Po najetí myší na řádek se vlevo od overlaye otevře podokno s deskou daného hráče. Miniony
v něm nejsou řádky, ale kartičky vedle sebe podle kapitoly 11.6.
U lokálního hráče jde o živou desku, u ostatních o poslední, kterou log ukázal, s číslem
kola. Dokud jste proti hráči nenastoupili, podokno to řekne místo prázdného seznamu. V Duos
se deska spoluhráče uloží z jeho souboje; do té doby podokno říká, že ji uvidíte po jeho
prvním souboji.

Následuje karta s vlastní deskou a pod ní buď `NABÍDKA BOBA`, nebo `DESKA SOUPEŘE` podle
toho, jestli právě běží souboj. Každý řádek ukazuje pozici, hvězdičku u zlaté karty,
jméno, klíčová slova celým jménem (`Taunt`, `Divine Shield`, `Reborn`, `Venomous`,
`Windfury`), útok u ikony meče, život u ikony srdce a tavern tier jako číslo v akcentu.
Statistiky nad tisíc se krátí přes `StatFormat.Compact` na `1k` nebo `2,4k`; v pozdních kolech
mají čtyři místa a do sloupců ani na kartičku se nevejdou. Zaokrouhluje se vždy dolů, aby
zkratka netvrdila víc, než minion doopravdy má.
Sloupce drží stejnou šířku napříč řádky přes `SharedSizeGroup`; bez něj si každý řádek
měří `Auto` sloupce sám a hodnoty se nezarovnají pod sebe.

Po najetí myší na řádek se vlevo ukáže tatáž kartička jako v podokně hráče, jen zvětšená
transformací na dvojnásobek, a pod ní řádek s typem minionu, pozicí a celými klíčovými
slovy. Do kartičky se klíčová slova vejdou jen oříznutá, protože má pevnou šířku.

Klik na nadpis kterékoli sekce ji sbalí a okno se o ni zmenší; sbalení platí do konce běhu,
trvalé schování sekce je v nastavení (11.7).

Panel `POSLEDNÍ UDÁLOSTI` se drží toho, co se týká vlastního hrdiny, respektive vlastního týmu
v Duos. Souboj hlásí kolo, soupeře, výsledek a poškození: `Kolo 12 · já vs Vanndar Stormpike:
výhra, dal jsem 34 dmg.`, v Duos `Kolo 12 · tým vs Vanndar Stormpike + Reno Jackson: výhra,
dali jsme 34 dmg.` Dané poškození log nehlásí zvlášť, počítá se z přírůstku tagu `DAMAGE`
na soupeřově hrdinovi za dobu souboje. Vyřazení jmenuje **hrdinu**, ne hráče: hrdina se pamatuje
lépe a v Duos ho log zná i u hráčů, jejichž BattleTag nikdy neodhalí. V Duos se hlásí i předání
karty spoluhráči: `Předal jsem spoluhráči: Proud Privateer.`

V Duos má řádek s dalším soupeřem tvar `Další soupeři: Overlord Saurfang + Snake Eyes · první
bojuje spoluhráč`: hrdinové v pořadí, v jakém nastoupí, a kdo začíná za náš tým. Nadpis vlastní
desky se během souboje mění na `DESKA SPOLUHRÁČE · Drek'Thar`, když na lokální straně stojí
spoluhráč, a nadpis soupeřovy desky nese jméno hrdiny, který na ní právě je. U minionů
v nabídce Boba se zeleně vypisuje `pár pro spoluhráče` nebo `triple pro spoluhráče`, když to
hra na kartě značí; podrobnosti v kapitole 8.8.

Výsledek souboje se dozvídáme po částech — nejdřív jestli se vyhrálo, pak poškození jedné
a druhé strany — a mezi tím se do panelu vejdou i jiné události. `TrackerState.UpdateEvent`
proto přepisuje už zveřejněnou hlášku na jejím místě; přepis jen poslední položky nestačil,
protože se mezi ni a doplněk vešlo vyřazení hráče.

Panel zobrazuje události od nejnovější po nejstarší. `TrackerState`
uchovává frontu maximálně šesti položek; po přidání sedmé zahodí nejstarší. Panel ukazuje
zvolený počet, dvě až šest (výchozí pět), a rezervuje si na ně místo, takže se scrollbar
objeví jen u delších zalomených textů.

V hlavičce je verze jako odznáček hned za názvem aplikace. Jeho výška je svázaná
s `ActualHeight` titulku, takže sedí na stejné lince i kdyby se velikost názvu změnila.
Ladicí build se pozná i barvou: okrový rámeček a okrové písmo místo tlumeného. Bez toho se dá snadno hodinu ladit něco, co
vůbec neběží — přesně to se stalo, když vydaný Release zůstal o dva dny starší než opravený
Debug.

Patička obsahuje stav a výsledek; diagnostika počtu zpracovaných řádků a rozpoznaných
událostí je po najetí myší na ni i na značku „BG“ v hlavičce. Vpravo na témže řádku jsou dvě
ikony: patch notes a ovládání. Ovládání se otevírá jako menu s položkami:

- **Vybrat log…** – standardní file dialog;
- **Spustit demo** – reset a syntetická data;
- **Pozastavit / Pokračovat** – zastaví nebo spustí timer;
- **Nastavení…** – totéž co ozubené kolo v hlavičce;
- **Vrátit okno na obrazovku** – vystředí okno na hlavní monitor;
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

### 11.3a Panel s detaily

Panel s detaily stojí buď ve sloupci vpravo od hlavního sloupce (204 návrhových jednotek plus
odsazení), nebo pod ním, podle nastavení. Je to jedna šablona `DetailsTemplate` ve dvou
hostitelích; vidět je vždy jen jeden. Má čtyři sekce:

| sekce | co ukazuje | odkud |
| --- | --- | --- |
| BONUSY PRO CELOU HRU | tavern kouzla, blood gemy, elementálové, piráti po řádcích | entita hráče, viz 8.6b |
| BONUSY NA KARTÁCH | karty na desce, které si samy zvyšují hodnotu | počítadla karty, viz 9.16 |
| TRINKETY | malý a velký slot: jméno trinketu, nebo odpočet do výběru; po najetí myší popis efektu | entity slotů, viz 9.14 |
| EKONOMIKA KRČMY | zlato, bonus toto kolo, utraceno v kole, cena rerollu, volné rerolly, cena upgradu, bonus příští kolo | tlačítka krčmy a entita hráče, viz 9.15 |

Bonusy zlata jsou dva a v panelu stojí vždycky oba, i když jsou nulové: **bonus toto kolo** je
zlato nad strop, které přišlo z karet zahraných minule (dočasné zlato), a **bonus příští kolo**
přibývá z karet zahraných teď. Naměřeno na jednom zápase: v kole 13 stálo `4/11` s bonusem
`+4` na příští kolo, v kole 14 pak `20/11` s bonusem `+9` v tomhle kole. Nulový bonus je
tlumený, nenulový zelený, takže je na první pohled poznat, jestli bonus je, nebo není.

Popis efektu trinketu se v tooltipu bere z databáze karet podle Card ID. To se získává ve dvou
krocích, protože hra u druhého slotu kartu v entitě nevymění vůbec: nejdřív z entity slotu,
a když v ní pořád leží prázdný slot, dohledá se podle jména mezi entitami s tagem
`BACON_TRINKET`, tedy mezi nabídkami k výběru. Ty kartu nesou vždycky.

Řádek je vždycky štítek vlevo a hodnota vpravo, takže se hodnoty všech sekcí zarovnají na
jednu linku. Bonusy a trinkety začínají tlumené a rozsvítí se tím, že v dané hře nastanou;
řádek tedy nikdy nepřibude ani nezmizí, jen změní barvu, a panel se pod rukou nehýbe. Volné
rerolly a zlato navíc se naopak ukazují jen tehdy, když je hráč skutečně má, protože po většinu
hry nejsou. Nula a neznámo se rozlišují: cena rerollu nula je `zdarma`, chybějící hodnota je
pomlčka a na posledním tieru je místo ceny upgradu `max`.

Panel se skrývá tlačítkem v hlavičce nebo v nastavení, kde se volí i jeho umístění. Vpravo
zmizí i jeho sloupec v mřížce, takže se karta zúží zpátky na 380 jednotek a okno na původní
šířku; naměřeno 594 px s panelem a 380 px bez něj. Se schovaným panelem se bonusy vrátí na
jeden řádek do karty lobby, aby o ně uživatel nepřišel — a naopak se s panelem ten řádek
schová, aby tatáž informace nestála na dvou místech.

### 11.3b Proužek s hudbou

Nad spodní lištou se ukazuje, co právě hraje: obal, název, interpret se zdrojem a tlačítka
předchozí, přehrát nebo pozastavit a další. Zdrojem je systémové rozhraní pro média
`Windows.Media.Control`, tedy totéž, co obsluhuje okénko u tlačítek hlasitosti. Proto
proužek vidí Spotify, YouTube v prohlížeči, YouTube Music i cokoli dalšího, co se hlásí
Windows, **bez přihlašování, bez klíčů k API a bez Premium**.

Ostatní cesty tuhle vlastnost nemají. Spotify Web API vyžaduje pro `/me/player` Premium
a nová aplikace smí mít v *development mode* jen pět ručně povolených uživatelů; *extended
quota mode* chce firmu s 250 000 aktivními uživateli měsíčně. YouTube navíc svými pravidly
pro vývojáře zakazuje přehrávač, který není vidět, oddělování zvuku od videa, stahování
i přístup mimo oficiální API, takže hudba z YouTube na pozadí legální cesta není.

Sledovač je `MediaSessionWatcher` a stojí na naměřeném chování:

- **Události se hlásí, ale dvakrát po sobě.** Proto je `NowPlaying` záznam se srovnáním
  podle hodnot a shodný stav se zahazuje, jinak by se rozhraní překreslovalo dvojmo.
- **Pozice ve skladbě stojí na místě.** Přehrávače ji do systému průběžně neposílají,
  takže ukazatel postupu by lhal a v proužku žádný není.
- **Identifikátor aplikace není na pohled k ničemu.** Edge se hlásí jako `MSEdge`, Spotify
  jako `Spotify.exe` a Media Player jako `Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic`.
  Překlad na čitelné jméno dělá `MediaSourceName.Friendly` a neznámý identifikátor se jen
  očistí, aby v proužku nezůstalo prázdno.
- **Tlačítka se řídí přehrávačem.** Prohlížeč s jedním videem nehlásí další ani předchozí
  skladbu, takže se ta tlačítka zeslabí a nejdou zmáčknout.

Události přicházejí na vlastním vlákně a přepočet se vrací na vlákno rozhraní; souběžné
žádosti se slučují. Názvy a obal se vyzvedávají jen při změně skladby, protože to jsou
volání do cizího procesu, zatímco stav přehrávání je zdarma. Každé dvě sekundy jde ještě
záchranný přepočet, kdyby některý přehrávač změnu neohlásil. Obal se čte přes `DataReader`,
protože rozšíření pro převod WinRT streamů v .NET 8 už není, a výsledný obrázek se zmrazí,
aby se dal použít z vlákna rozhraní.

Celé čtení i každý příkaz jsou v `try`: relace patří cizímu procesu, který může zmizet
uprostřed volání, a proužek s hudbou nesmí shodit tracker. Když se rozhraní vůbec
neotevře, proužek se jen neobjeví.

Kvůli WinRT má `Tracker.Desktop` cíl `net8.0-windows10.0.19041.0`. Nejnižší podporovaná
verze systému je tím Windows 10 verze 2004 a vydaná binárka roste o 6 MiB.

### 11.3c Historie zápasů a MMR

Sekce `HISTORIE` ukazuje posledních tři až deset zápasů (výchozí pět) zvoleného režimu,
nejnovější první: datum, hrdinu (v Duos i spoluhráče), umístění, změnu MMR a zůstatek MMR.
Přepínač `SOLO` / `DUOS` se pamatuje v nastavení, aby po startu neskákal, a štítek v nadpisu
nese aktuální MMR zvoleného režimu. Umístění je zlaté za první místo, zelené za horní
polovinu; změna MMR zelená za zisk, červená za ztrátu.

Zápas se do historie zapíše ve chvíli, kdy hra skončí při běžícím trackeru: `MatchRecorder`
pozná přechod z rozehrané hry do ukončené a ještě před uzavřením archivu složí
`MatchRecord` ze stavu trackeru (`MatchHistory.FromState`): hrdina a jeho karta, umístění
z `FinalPlace`, režim, počet kol, čas konce. Identifikátorem je jméno archivu zápasu
(`match-yyyyMMdd-HHmmss-fff`), takže záznam a log k přehrání patří k sobě a tentýž zápas se
nezapíše dvakrát, ani když se po restartu trackeru dohraný zápas obnoví z archivu. Hra, která
se nedostala do prvního kola, se nezapisuje; to je odchod z lobby při výběru hrdiny. Zápasy
dohrané bez běžícího trackeru v jiné relaci hry se nezapíšou vůbec, protože se jejich log
nečte.

MMR hra do logů nepíše (9.5). Pole v posledním sloupci je proto editovatelné: uživatel do něj
opíše zůstatek, který mu hra ukázala, a potvrdí Enterem nebo opuštěním pole; Escape vrátí
původní hodnotu, prázdné pole zůstatek smaže. Změna se počítá jako rozdíl proti nejbližšímu
předchozímu zápasu téhož režimu se známým zůstatkem, takže doplnění jednoho čísla přepočítá
i řádek pod ním, a sólo a Duos se nemíchají. Aktuální MMR je zůstatek z posledního zápasu, kde
je doplněný.

Historie leží v `history.json` ve složce dat, ukládá se hned při každé změně přes dočasný
soubor a drží nejvýš `MatchHistory.Capacity` (500) záznamů. Na retenci archivu nezávisí:
i když se log zápasu už dávno smazal, jeho řádek v historii zůstává. Řádky ve view modelu
porovnávají obsah (`Equals`), aby `Sync` nepřepisoval nezměněné řádky a nezavíral právě
editované pole; po zápisu MMR se řádek nahradí novým, protože se změnil.

### 11.4 Aktualizace kolekcí

`MainViewModel` kolekce nepřepisuje přes `Clear` a znovunaplnění, ale porovnává je položku
po položce a mění jen to, co se skutečně liší. Kompletní přepis by při každém ticku
resetoval posun v panelu událostí a seznamy by problikávaly.

Položky jsou `record`, takže porovnání je strukturální a bez ručně psané rovnosti. Deska
uvnitř řádku lobby je výjimka: porovnává se podle reference, a view model proto vrací
stejnou instanci, dokud se obsah desky nezmění. Jinak by se řádek nahrazoval při každém
ticku a zavíral právě otevřené podokno.

### 11.5 Styly ovládacích prvků

Všechny styly jsou v `Tracker.Desktop/Themes/Controls.xaml`, které `App.xaml` jen připojí:
tlačítka s textem, ikonová a zavírací tlačítka, hlavička sekce jako přepínač se šipkou,
přepínač ano/ne, segmentový přepínač, vzorek akcentu, posuvník, navigace nastavení,
ukazatel postupu, textové pole, tooltip, kontextové menu a tenký scrollbar bez dráhy, který
ukazuje jen jezdec. Nativní světlé prvky se proto nikde nepoužívají a přepnutí motivu
překreslí i je, protože berou barvy přes `DynamicResource`.

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
tmavém podkladu, popis se vynechá a všechny ostatní údaje zůstanou čitelné. Kresby se dají
v nastavení vypnout úplně; pak zůstane jen tmavý ovál a nic se nestahuje.

### 11.7 Nastavení

Okno nastavení otevírá ozubené kolo v hlavičce nebo položka v menu patičky. Je jen jedno,
sedí nad overlayem (`Owner`, `Topmost`) a používá stejný `WindowChrome` jako patch notes,
takže se táhne za titulek a zvětšuje ze všech stran. Vlevo je navigace se čtyřmi stránkami,
vpravo řádky „popis vlevo, ovládací prvek vpravo“:

| stránka | co nastavuje |
| --- | --- |
| Vzhled | motiv (tmavý, světlý), akcent (šest barev), krytí okna 50 až 100 %, zvětšení 60 až 160 %, strop podle výšky obrazovky a jeho podíl, hustota lobby, kresby karet |
| Rozložení | přehled, lobby (řádek s dalším soupeřem, typy minionů, BattleTagy), desky, ruka, detaily a jejich umístění, události a jejich počet, historie a počet zápasů v ní, hudba |
| Chování | vždy navrchu, pamatovat polohu okna, spouštět sbalené, kontrolovat aktualizace |
| Data | instalace Hearthstonu mimo obvyklé cesty, počet uložených zápasů k přehrání (1 až 200), složka dat s tlačítkem do Průzkumníka, co aplikace posílá na síť |

Formulář je svázaný přímo s `UserSettings`, obalem nad `TrackerSettings` z Core, který
každou změnu ohlásí. Hlavní okno na ni reaguje hned: motiv a akcent přes `ThemeManager`,
zvětšení přepočtem velikosti, umístění detailů přesunem panelu, ostatní přes vazby
viditelnosti v XAML a odvozené hodnoty ve view modelu (výška řádku lobby, počet událostí).
Tlačítko Uložit není; `SettingsStore` zapíše `settings.json` půl sekundy po poslední změně
a při zavření okna, přes dočasný soubor, aby po pádu uprostřed zápisu nezůstal useknutý JSON.
Tažení okna se do nastavení zapisuje jako poloha, tažení za roh jako zvětšení.

Soubor je čitelný JSON s výčty jako slova (`"Theme": "Light"`), aby se dal upravit ručně.
Chybějící klíče dostanou výchozí hodnotu, cizí klíče se ignorují a hodnoty mimo rozsah srovná
`TrackerSettings.Normalized`, aby ručně upravený soubor nemohl vyrobit neviditelné nebo obří
okno. Poškozený soubor dá výchozí nastavení a nikdy aplikaci nezastaví.

Přepínače výčtů jsou `RadioButton` svázané přes `EnumEqualsConverter`: dopředu říká, jestli
je právě tahle možnost vybraná, zpět při zaškrtnutí zapíše hodnotu. Vzorky akcentu nesou
barvu v `Tag` jako text; v šabloně ji na štětec převede obyčejná vazba, protože
`TemplateBinding` typ nepřevádí a vzorek by zůstal prázdný. Instalaci hry vybírá
`OpenFolderDialog` z .NET 8.

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

Verze se drží v `VersionPrefix` v `Directory.Build.props`. Build, který neprošel vydáním, k ní
dostane příponu přes `VersionSuffix`, takže hlásí `0.9.3-dev`, kdežto vydání `0.9.3`.

**Podmínka je na `Version`, ne na `Configuration`.** Původně stálo
`Condition="'$(Configuration)' == 'Debug'"`, což vypadá správně, ale `Directory.Build.props` se
vyhodnocuje **před** tím, než SDK dosadí výchozí konfiguraci. Změřeno přes
`dotnet msbuild -getProperty:VersionSuffix`: bez `-c` na příkazové řádce byl `Configuration`
prázdný, podmínka neplatila a `dotnet build` bez parametru vyrobil binárku, která se tvářila
jako vydaná verze. Zjistilo se to tak, že si takový build spustil uživatel a chybějící `-dev`
mu neřeklo, že hraje se starším sestavením.

`Version` naopak zadává jedině vydání (`-p:Version=…` v `release.yml`), takže se jako vydané
nemůže označit nic, co vydáním neprošlo — ani Release build ze stroje. Naměřené chování:

| build | verze |
| --- | --- |
| `dotnet build` | `0.9.3-dev` |
| `dotnet build -c Debug` | `0.9.3-dev` |
| `dotnet build -c Release` | `0.9.3-dev` |
| `dotnet publish -c Release -p:Version=0.9.3` (vydání) | `0.9.3` |

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

Test suite aktuálně obsahuje sto dvacet devět testů.

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

Jedenáct v `DuosTests` pokrývá režim Duos: rozpoznání a spárování dvojic z tagů, řazení týmů se
zachováním dvojic pohromadě, pravidlo jeden BattleTag = jeden slot, udržení identity lokálního
hráče na jeho slotu i tehdy, když spoluhráč bojuje první, a to, že se jedno jméno nikdy
neobjeví na dvou řádcích tabulky zároveň; dále pojmenování obou soupeřů v hlášce o souboji
s první hodnotou zdvojeného poškození, ohlášení celého týmu naráz díky sdíleným životům,
zrcadlení životů mezi spoluhráči, uložení desek všech čtyř účastníků souboje pod hrdiny, kteří
s nimi bojovali, čtení toho, kdo bojuje první, spolu s nápovědou páru pro spoluhráče, a hlášku
o kartě předané spoluhráči.

Jeden v `BattlegroundsStateTests` navíc ověřuje umístění vyřazených hráčů: počítá se z počtu
těch, kdo zůstali ve hře, a dva pády v jednom kole řadí zbývající životy, ať tagy `DAMAGE`
přišly v jakémkoli pořadí.

Čtyři v `SettingsTests` pokrývají nastavení: uložení a načtení každé hodnoty do čitelného
JSON s výčty jako slova, výchozí hodnoty při chybějícím i poškozeném souboru, ignorování
cizích klíčů a doplnění chybějících, a srovnání hodnot mimo rozsah. Jeden v `AppPathsTests`
ověřuje, že složku dat přesměruje proměnná `BGTRACKER_DATA_DIR` a bez ní zůstává pod
LOCALAPPDATA.

Šest v `MatchHistoryTests` pokrývá historii zápasů: zápis dohraného zápasu ze stavu
trackeru přes `MatchRecorder`, vynechání hry bez prvního kola, změny MMR počítané zvlášť
v sólu a v Duos i aktuální MMR, výběr posledních zápasů jednoho režimu, dedupliaci podle
identifikátoru s uložením a načtením včetně poškozeného souboru, a identifikátor ze jména
archivu. `MatchLogArchiveTests` navíc ověřují nastavitelnou retenci: archiv drží tolik
zápasů, kolik dostane, a snížení za běhu ořeže složku hned.

Dva v `MatchLogArchiveTests` pokrývají kompresi: zabalení dohraného zápasu se zachováním
obsahu i řádovým zmenšením a dobalení pozůstalých prostých logů s ořezem retence.

Čtrnáct v `StatFormatTests` pokrývá zkrácený zápis statistik: hranice tisíce, desetinu jen do
deseti tisíc, zaokrouhlování vždy dolů a zástupný znak u neznámé hodnoty.

Poslední ověřený build prošel s 0 warnings a 0 errors; všech 129 testů prošlo.
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
17. **Sbalení sekcí za běhu se nepamatuje.** Šipky u nadpisů platí do konce běhu; trvalé je
    jen vypnutí sekce v nastavení. Poloha okna a zvětšení se naopak pamatují.
18. **Poškozený `settings.json` se potichu nahradí výchozími hodnotami.** Stačí jedna
    neplatná hodnota výčtu a celý soubor se zahodí; aplikace o tom nic neřekne, jen se otevře
    ve výchozím vzhledu.
18a. **MMR se nečte z hry.** Žádný log hry ho neobsahuje (9.5), takže ho uživatel opisuje do
    historie ručně; bez toho zůstane změna i zůstatek prázdný. Automatické čtení by
    znamenalo číst paměť procesu hry jako HearthMirror, což je mimo záměr trackeru.
    Zápasy dohrané bez běžícího trackeru v jiné relaci hry se do historie nedostanou.
19. **Jména spoluhráčů v Duos nejsou vždy k mání.** Log pojmenuje jen hráče, jejichž entita
    se v něm objevila; ostatní zůstanou jako `Skrytý hráč`. Vlastní spoluhráč jméno nedostane
    nikdy, protože jeho hrdinu hra věší na entitu lokálního hráče. Jeho deska se ukládá jen
    ze souboje, takže do jeho prvního souboje je podokno prázdné, a karta, kterou spoluhráč
    předal, se v ruce od koupené karty nijak neliší.
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
