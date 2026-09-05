# Changelog

Všechny podstatné změny v tomto projektu. Formát vychází z
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), číslování se drží
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

Protože je hlavní číslo verze nula, jde podle bodu 4 specifikace o počáteční vývoj:
veřejné rozhraní se může měnit i mezi vedlejšími verzemi. Pravidla, kdy se které číslo
zvyšuje, jsou v `documentation.md`, sekce 14.1.

## [Nevydáno]

## [0.14.0] - 2026-09-05

### Přidáno

- **Průvodce připojením** pro případ, že tracker zůstává v režimu NASLOUCHÁM: čtyři kroky
  se stavem (hra běží, logování v `log.config`, složka s hrou, `Power.log` běžící hry)
  a tlačítka, která je opraví. **Zapnout logování** doplní do `log.config` sekci `[Power]`,
  ostatní obsah nechá a původní soubor zálohuje jako `log.config.bak`; **Prohledat disky**
  najde instalaci ve vlastní složce (třeba `D:\Hry\Hearthstone`); **Vybrat složku…** ji
  uloží do nastavení; **Spustit tracker jako správce** pomůže, když hra běží jako správce.
  Kontrola se opakuje každé dvě sekundy. Průvodce se otevírá z pruhu v overlayi, z menu
  patičky, z nastavení (Data), argumentem `--setup` a sám při prvním spuštění, když hra
  nemá zapnuté logování.
- Pruh v overlayi při naslouchání říká jednou větou, na co tracker čeká (hra neběží, čeká
  se na první zápas, chybí logování, nenašla se instalace); varovně se zbarví, když bez
  zásahu uživatele nemá jak skončit.

### Opraveno

- Hra spuštěná jako správce: tracker nemohl přečíst start jejího procesu, žádný log
  nepovažoval za aktuální a zůstal navždy v naslouchání. Teď v takovém případě bere log,
  do kterého hra psala v posledních deseti minutách.

## [0.13.0] - 2026-09-05

### Přidáno

- **Historie zápasů** jako nová sekce overlaye: posledních tři až deset zápasů (výchozí pět)
  s datem, hrdinou (v Duos i spoluhráčem), umístěním, změnou MMR a zůstatkem MMR, přepínač
  **SOLO / DUOS** a štítek s aktuálním MMR zvoleného režimu. Zápas se zapíše, jakmile hra
  skončí při běžícím trackeru, a historie přežívá restart i ořez archivu (`history.json`).
- **MMR** doplňuje uživatel: hra rating do žádného logu nepíše (ověřeno v `Power.log`,
  `Hearthstone.log`, `GameNetLogger.log`, `LoadingScreen.log`, `Achievements.log` i Unity
  `Player.log`), a číst paměť procesu hry tracker záměrně nechce. Po zápase se do řádku opíše
  zůstatek z obrazovky hry a tracker spočítá změnu (`+59`, `−81`) proti předchozímu zápasu
  téhož režimu i aktuální MMR. Sólo a Duos se nemíchají.
- Nastavení **Uložené zápasy k přehrání**: kolik dohraných zápasů zůstává v archivu pro
  Vybrat log, 1 až 200 (dřív napevno pět). Snížení ořeže složku hned.
- Nastavení **Historie zápasů** a **Zápasů v historii** na stránce Rozložení.

## [0.12.0] - 2026-09-05

Nový vzhled overlaye a okno nastavení. Všechny dosavadní funkce zůstávají; mění se, jak
vypadají a kolik místa zabírají.

### Přidáno

- **Nastavení** pod ozubeným kolem v hlavičce a v menu patičky, ve čtyřech stránkách. Vzhled:
  tmavý a světlý motiv, šest akcentů, krytí okna, zvětšení s volitelným stropem podle výšky
  obrazovky, hustota lobby, kresby karet. Rozložení: zapínání každé sekce (přehled, lobby
  s řádkem soupeře, typy minionů a BattleTagy, desky, ruka, detaily s umístěním vpravo nebo
  dole, události s počtem, hudba). Chování: vždy navrchu, pamatování polohy okna, start
  sbalený, kontrola aktualizací. Data: instalace Hearthstonu mimo obvyklé cesty a složka dat.
  Změny se projeví hned a ukládají se samy do `settings.json`; soubor je čitelný a dá se
  upravit ručně, chybějící nebo poškozené hodnoty nahradí výchozí.
- Sekce **Ruka**: karty v ruce včetně tavern kouzel, ve výchozím stavu vypnutá.
- Poloha okna se pamatuje mezi spuštěními; velikost se pamatuje jako zvětšení, které se mění
  i tažením za pravý dolní roh okna.
- Každá sekce má v nadpisu šipku na sbalení; dřív šlo sbalit jen desky.
- Proměnná prostředí `BGTRACKER_DATA_DIR` přesměruje složku dat (nastavení, archiv zápasů,
  mezipaměti karet, data prohlížeče). Hodí se pro přenosnou instalaci a pro snímky rozhraní
  nad čistými daty; `LOCALAPPDATA` .NET na Windows nečte.

### Změněno

- **Kompaktní rozložení.** Hlavní sloupec má 380 návrhových jednotek místo 500, hlavička 42
  místo 64, řádek lobby 22 místo 38 a řádek minionu 15 místo 18. Výška okna sleduje obsah:
  co se schová nebo sbalí, o to je okno menší. Na FullHD zabere výchozí overlay 594 × 808 px
  s panelem detailů a 380 × 808 bez něj; dřív 618 × 978 a 420 × 978.
- **Design systém** v `Themes/Controls.xaml`: jedna paleta pro overlay, nastavení i patch
  notes, ikonová tlačítka bez podkladu, tenké scrollbary, přepínače, posuvníky, segmentové
  přepínače, štítky. Písmo Segoe UI Variable. Barvy se přepínají za běhu bez restartu.
- Boční panel se stal panelem s detaily, který stojí vpravo nebo dole podle nastavení;
  tlačítko v hlavičce ho skrývá jako dřív a se skrytým panelem se bonusy vrátí na řádek do
  lobby.
- Hlavička je nižší a nese režim čtení, zdroj, verzi a v Duos štítek; tlačítka sbalení
  a zavření jsou ikonové.
- Menu v patičce má navíc položku **Nastavení…**; diagnostika parseru je po najetí na patičku
  a na značku „BG“.

### Odebráno

- Automatické sbalení desek na nízkém monitoru a pevné návrhové výšky 1163 a 879. Nahradil je
  strop zvětšení podle výšky obrazovky a vypínání sekcí v nastavení.

## [0.11.0] - 2026-09-05

Revize režimu Duos nad pěti skutečnými zápasy. Sólo se mění jen v umístění vyřazených hráčů.

### Opraveno

- **Poškození v Duos bylo dvojnásobné.** Když lokální hráč bojoval první a tým prohrál, zapíše
  hra `DAMAGE_DEALT_TO_HERO_LAST_TURN` dvakrát: za soubojovou kopii spoluhráče a znovu za
  vlastního hrdinu, takže druhá hodnota je dvojnásobek (4→8, 12→24, 15→30). Tracker bral tu
  druhou; skutečný úbytek životů odpovídal ve všech měřených zápasech té první, a ta se teď
  bere. V sólu se nic nemění.
- **Deska spoluhráče se ukládala pod lokálního hráče.** Když spoluhráč bojoval první, stála
  na lokální straně desky jeho sestava a tracker ji zapsal jako vlastní. Desky se teď ukládají
  pod hrdinu, který s nimi doopravdy bojoval; pro každou stranu se sleduje, kdo na ní stojí.
- **Tým se hlásil jako vyřazený dvakrát a s předčasnou hláškou.** Dvojice sdílí životy a hra
  píše stejné hodnoty na oba hrdiny, jen s odstupem; mezi tím tracker hlásil `X vypadl, tým
  hraje dál.` a potom ještě celý tým. Životy se teď mezi spoluhráči zrcadlí a tým padá naráz.
- **Umístění vyřazeného hráče bylo živé pořadí, ne konečné místo.** Tag umístění v okamžiku
  vyřazení nese ještě pořadí z doby, kdy hráč žil; hráč vyřazený jako pátý se hlásil na
  druhém místě a tým vyřazený jako druhý na prvním. Umístění se počítá z počtu těch, kdo
  zůstali ve hře, a dva pády v jednom kole řadí zbývající životy. Platí pro sólo i Duos.
- Hláška o souboji v Duos jmenovala jen prvního soupeře. Souboj je týmový: druhý z dvojice se
  přidá, jakmile padne některá deska, a v logu se na soupeřově straně vystřídají oba. Hláška
  teď zní `Kolo 3 · tým vs Overlord Saurfang + Snake Eyes: prohra, dostali jsme 4 dmg.`

### Přidáno

- Desky **obou soupeřů i spoluhráče** ze souboje. Podokno u řádku spoluhráče ukazuje jeho
  poslední desku s číslem kola, stejně jako u soupeřů; do jeho prvního souboje řekne, že ji
  uvidíte po něm.
- Nadpis vlastní desky během souboje říká, čí deska na lokální straně právě stojí:
  `DESKA SPOLUHRÁČE · Drek'Thar`. Nadpis soupeřovy desky nese jméno hrdiny, který na ní právě
  je, protože se během jednoho souboje vystřídají oba.
- Řádek s dalším soupeřem v Duos jmenuje oba hrdiny dvojice v pořadí, v jakém nastoupí, a říká,
  kdo začíná za náš tým: `Další soupeři: Overlord Saurfang + Snake Eyes · první bojuje
  spoluhráč` (tag `BACON_DUO_PLAYER_FIGHTS_FIRST_NEXT_COMBAT`).
- Nápověda **pár pro spoluhráče** a **triple pro spoluhráče** u minionů v nabídce Boba, tak jak
  to hra značí ikonou na kartě (tagy `BACON_DUO_PAIR_CANDIDATE_TEAMMATE`
  a `BACON_DUO_TRIPLE_CANDIDATE_TEAMMATE`).
- Hláška o kartě předané spoluhráči: `Předal jsem spoluhráči: Proud Privateer.` (tag
  `IS_USING_PASS_OPTION`).

### Změněno

- Řazení týmů v žebříčku bere vyšší z obou hodnot zbývajících životů dvojice, ne součet, který
  sdílené životy počítal dvakrát. Na pořadí to nic nemění.
- V hláškách o souboji se v Duos píše `dostali jsme` a `dali jsme`; poškození dostává a dává
  tým.

## [0.10.1] - 2026-09-04

### Opraveno

- Log předaný na příkazové řádce (`--log`) se přehraje, místo aby se z něj stal živý zápas.
  Dřív se takový log založil do archivu jako právě odehraná hra a přepsal checkpoint; protože
  se archiv drží na pěti posledních zápasech, opakované spuštění vytlačilo skutečně odehrané
  hry. Cesta k logu běžící hry se pořád čte živě, takže ruční výběr aktuálního logu funguje
  jako dřív.

### Přidáno

- **Útok undeadů** jako pátý bonus pro celou hru. Na entitě hráče ho hra nedrží — tag
  `UNDEAD_ATTACK_BUFF` sice v enumu hry existuje, ale v žádném pozorovaném logu nepadl.
  Hodnotu nese enchantment hráče `BG25_011pe` v `TAG_SCRIPT_DATA_NUM_1`; naměřeno na kartě
  Nerubian Deathswarmer, kde vyrostla z 5 na 25 a v jiné hře až na 255. Život se u undeadů
  takhle nebuffuje, proto má tenhle bonus jen jednu hodnotu.
- Boční panel vpravo od hlavní karty se třemi sekcemi. **Bonusy pro celou hru** jsou v něm
  rozepsané po řádcích místo jedné zkrácené věty: tavern kouzla, blood gemy, elementálové,
  piráti a útok undeadů. **Trinkety** ukazují jméno vybraného trinketu, a
  dokud je slot prázdný, odpočet do výběru v kolech. **Ekonomika krčmy** vypisuje zlato
  k utracení i strop kola, kolik už v kole padlo, cenu rerollu, počet volných rerollů, cenu
  upgradu tavernu a oba bonusy zlata.
- Panel se skrývá tlačítkem v hlavičce; okno se pak zúží zpátky na původní šířku a bonusy se
  vrátí na řádek do karty lobby.
- Nové čtené hodnoty: cena rerollu a volné rerolly z tlačítka krčmy, zlato navíc na příští
  kolo z entity hráče, strop tavern tieru a oba sloty na trinkety s odpočtem.
- Po najetí myší na trinket se ukáže popis jeho efektu z databáze karet. Card ID se hledá ve
  dvou krocích, protože u druhého slotu ho hra v entitě nevymění: nejdřív z entity slotu, pak
  podle jména mezi nabídkami k výběru.
- Sekce **Bonusy na kartách** vypisuje karty na desce, které si samy zvyšují hodnotu, tedy ty
  s textem „and improve this“. Hra je drží na kartě, ne na entitě hráče, a text karty přitom
  pořád ukazuje výchozí čísla: Spark Snapperův satelit vyrostl z 2/2 na 28/28, což jinde než
  v jeho počítadle není.
- Ekonomika má dva trvalé řádky s bonusy zlata: **bonus toto kolo** je zlato nad strop, které
  přišlo z karet zahraných minule, **bonus příští kolo** přibývá z karet zahraných teď. Oba se
  ukazují i v nule, aby bylo vidět i to, že bonus není. Bez prvního z nich vypadal poměr
  `20/11` jako chyba.

### Změněno

- Řádky v ekonomice se jmenují **cena rerollu** a **cena upgradu**, aby bylo poznat, že to
  není počet, ale cena ve zlatě. Popisky mají k tomu vysvětlení po najetí myší.

### Opraveno

- Build, který neprošel vydáním, se netvářil jako vydaná verze — ale jen když se stavěl
  s `-c Debug`. Podmínka na příponu `-dev` byla v `Directory.Build.props` na `Configuration`,
  a ten tam ještě nemusí být dosazený, protože se soubor vyhodnocuje před výchozími hodnotami
  SDK. `dotnet build` bez parametru tak vyrobil binárku hlásící `0.9.3` místo `0.9.3-dev`.
  Podmínka je teď na `Version`, kterou zadává jedině vydání, takže značku dostane i Release
  build ze stroje.
- Karty s vlastním počítadlem ukazovaly obě čísla jako `20/22`, což tvrdilo, že je bonus
  nesouměrný. Změřeno, že jedno počítadlo drží nasčítaný přírůstek a druhé výslednou hodnotu:
  při `NUM_1` = 26 a `NUM_2` = 28 dostal minion na desce +28/+28. Vypisuje se proto jen to
  vyšší jako jedno číslo.

## [0.9.3] - 2026-09-03

### Přidáno

- Proužek s hudbou nad spodní lištou: co právě hraje, obal, interpret a tlačítka předchozí,
  přehrát nebo pozastavit a další. Čte se ze systémového rozhraní pro média
  (`Windows.Media.Control`), takže bez jakéhokoli přihlašování, klíčů k API a bez Premium
  ovládá Spotify, YouTube v prohlížeči, YouTube Music i cokoli dalšího, co se hlásí
  Windows. Tlačítka se řídí tím, co přehrávač podporuje, a proužek se schová, když nic
  nehraje. Ukazatel postupu ve skladbě záměrně chybí: přehrávače pozici do systému
  průběžně neposílají, takže by stál na místě.

### Změněno

- Cíl `Tracker.Desktop` je `net8.0-windows10.0.19041.0` místo `net8.0-windows`, protože
  bez verze Windows v cíli nejsou vidět WinRT rozhraní. Nejnižší podporovaná verze systému
  je tím Windows 10 verze 2004; vydaná binárka kvůli projekci WinRT roste o 6 MiB.

## [0.9.2] - 2026-09-03

### Opraveno

- Sbalení overlaye zdrobnilo celé okno, místo aby nechalo vrchní lištu. Od zavedení
  `Viewboxu` v 0.7.0 škáluje jedno zvětšení celou kartu, takže samo skrytí obsahu nestačilo:
  karta si držela plnou návrhovou výšku a okno snížené na 64 bodů z ní udělalo asi 27 bodů
  širokou miniaturu. Sbalený overlay teď návrhovou výšku snižuje na hlavičku, drží šířku
  rozbaleného okna a hlavička zůstává v původní velikosti.
- Změna monitorů ve sbaleném stavu okno nafoukla na minimální výšku rozbaleného rozložení
  (615 bodů) s prázdným obsahem, protože přepočet nastavoval `MinHeight`, ale výšku už ne.

## [0.9.1] - 2026-09-03

### Opraveno

- Okno se nedalo vrátit, když jeho hlavička skončila mimo monitory. Overlay se dá přetáhnout
  jen za hlavičku, takže po odpojení druhého monitoru, po změně rozlišení nebo když se okno
  otevřelo na menší obrazovce, než na jakou je spočítané, zůstalo nedosažitelné. Hlavička se
  teď automaticky stahuje na plochu monitorů, a to při startu, při každém přepočtu velikosti
  i po změně monitorů. Vodorovná poloha se zachová, takže okno neuteče z monitoru, na kterém
  ho uživatel má.
- Hra nainstalovaná mimo výchozí složku se nenašla. Cesta k instalaci se teď čte z registry
  (`InstallLocation` v odinstalačním klíči), takže se najde i na jiném disku a ve vlastní
  složce. Kromě toho se na každém pevném disku zkouší devět běžných umístění místo dvou:
  `Hearthstone`, `Games\Hearthstone`, `Program Files\Hearthstone`,
  `Program Files (x86)\Hearthstone`, `Battle.net\Hearthstone`, `Blizzard\Hearthstone`,
  `Blizzard Entertainment\Hearthstone`, `Games\Blizzard\Hearthstone`
  a `Games\Battle.net\Hearthstone`.

### Přidáno

- Položka menu **Vrátit okno na obrazovku** vystředí overlay na hlavní monitor v návrhové
  velikosti. Poslední záchrana, když okno skončí mimo obrazovku.
- Když se log nenajde, tooltip u stavu vypíše proč: jestli hra běží, jestli má `log.config`
  sekci `[Power]` a ve kterých instalacích se hledal adresář `Logs`. Dosud vypadaly všechny
  tyhle případy stejně jako „čekám na nový Power.log“.

## [0.9.0] - 2026-09-03

### Přidáno

- Ikona patch notes ve spodní liště otevře okno s patch notes Hearthstonu ve vestavěném
  prohlížeči. Okno se drží nad hrou, jde přesunout, zvětšit tažením za kteroukoli hranu,
  maximalizovat i zavřít, a má tlačítka zpět, znovu načíst a otevřít v systémovém
  prohlížeči. Když na stroji chybí runtime WebView2, odkaz jde rovnou do systémového
  prohlížeče, takže aplikace na runtimu není závislá.

### Změněno

- Čtyři tlačítka ve spodní liště (vybrat log, spustit demo, pozastavit, restart) se
  schovala do rozklikávacího menu. Samostatný řádek s tlačítky zabíral v pevné výšce karty
  skoro 40 px.

## [0.8.1] - 2026-09-03

Chování aplikace se nemění: od 0.8.0 se v `src/` nezměnil ani jeden řádek a binárka je
funkčně totožná. Mění se způsob vydávání a tohle je první vydání, které novým postupem
projde celé.

### Přidáno

- Tento changelog. Sekce vydané verze slouží zároveň jako popis vydání na GitHubu,
  takže vydání bez záznamu v changelogu neprojde.
- `scripts/verify-version.ps1` kontroluje, že tag, `VersionPrefix`
  v `Directory.Build.props` a záznam v changelogu drží pohromadě a odpovídají
  Semantic Versioning 2.0.0. Skript běží v CI i před každým vydáním.

### Změněno

- GitHub akce zvednuté na verze, které cílí na Node.js 24: `actions/checkout` v4 → v7,
  `actions/setup-dotnet` v4 → v6, `softprops/action-gh-release` v2 → v3. Node.js 20 je
  na runnerech odepsaný a dosud se běh na Node.js 24 vynucoval s varováním.
- Neplatný tag vydání skončí chybou. Dosud se z něj potichu odvodila verze `0.0.0`
  a vydání se přesto vypublikovalo.

## [0.8.0] - 2026-09-03

### Přidáno

- Počítadlo bonusů platných pro celou hru. Hra je drží na entitě hráče, ne na minionech:
  tavern kouzla (`TAVERN_SPELL_*_INCREASE`), blood gemy (`BACON_BLOODGEMBUFF*VALUE`),
  elementálové a piráti (`BACON_ELEMENTAL_BUFF*VALUE`, `BACON_PIRATE_BUFF*VALUE`).
  V hlavičce je z nich řádek `Bonusy: …`, který ukazuje jen to, co v dané hře nastalo.

### Změněno

- Verze se přesunula ze řádku se stavem k názvu aplikace. Výška odznáčku je svázaná
  s výškou titulku.

### Opraveno

- Pruh s načítáním vybraného logu odřezával spodní lištu s tlačítky. Řádek s událostmi
  měl `MinHeight` 159, ale po odečtení ostatních řádků na něj v pevné výšce karty
  zbývá jen asi 147 px, takže mřížka přetékala.
- Návrat tagů globálních bonusů na nulu, kterým hra začíná každý souboj, se zahazuje.
  Bez toho by počítadlo v každém souboji na tři sekundy spadlo na `+0/+0`.

## [0.7.1] - 2026-09-02

### Přidáno

- Verze v hlavičce overlaye. Ladicí build hlásí příponu `-dev` a odliší se okrovým
  rámečkem, aby se nedal splést s vydanou verzí.

## [0.7.0] - 2026-09-02

### Přidáno

- Poslední události se drží vlastního hrdiny, v Duos vlastního týmu. Souboj hlásí kolo,
  soupeře, výsledek a poškození na obě strany.

### Změněno

- Rozložení se sází v návrhových jednotkách 500 × 1163 a je zabalené do `Viewbox`, takže
  overlay zabere stejný podíl obrazovky na FullHD i na 4K. Zvětšení se počítá z výšky
  pracovní plochy, ne z počtu pixelů.
- Čitelnější kartičky minionů.

## [0.6.1] - 2026-09-02

### Změněno

- Strop retence zápasů padá z třiceti na pět a ořez běží hned při otevření archivu.
- Uložený zápas se načítá na pozadí a nad tlačítky se ukazuje pruh s postupem. Půl
  milionu řádků se parsuje pár sekund a okno mezitím zamrzalo.
- V hlavičce je místo celého jména archivu datum a čas zápasu; celá cesta zůstala
  v podokně.

### Opraveno

- Pruhy aktualizace a načítání se ve stejném řádku mřížky překrývaly.

## [0.6.0] - 2026-09-02

### Přidáno

- Dohraný zápas se zabalí Brotli, rozehraný zůstává v textu, protože se z něj po
  restartu obnovuje. Volba kodeku podle měření na zápase o 64,4 MB: Brotli Optimal
  dá 29,0× za 152 ms, GZip Optimal 21,6× za 353 ms.
- Vybrat vlastní zápas ho jen přehraje v režimu ZÁZNAM: nic se nearchivuje a checkpoint
  zůstane, kde byl.

## [0.5.0] - 2026-09-02

### Přidáno

- Podpora režimu Duos. Osm hráčů tvoří čtyři dvojice a rozdávají se čtyři místa.
  Žebříček se seskupuje po týmech, protože `PLAYER_LEADERBOARD_PLACE` nese v Duos
  umístění týmu a oba spoluhráči mají stejné.

## [0.4.0] - 2026-09-01

### Změněno

- Kartičky minionů ve stylu hry: portrét v oválu, štít s tavern tierem, jmenná páska,
  rámeček s popisem efektu, páska s typem a drahokamy s útokem a životem.

### Přidáno

- `CardTextProvider` stahuje popisy efektů z databáze HearthstoneJSON; v logu nejsou.

## [0.3.0] - 2026-09-01

### Přidáno

- Kartičky minionů s kresbou karty. `CardArtProvider` drží mezipaměť
  v `%LOCALAPPDATA%\BattlegroundsTracker\cardart` a stahuje z CDN HearthstoneJSON;
  z instalace hry brát kresby nelze.

## [0.2.2] - 2026-09-01

### Opraveno

- Sloupce desek drží stejnou šířku napříč řádky přes `SharedSizeGroup`.
- Tavern tier se ukazuje jako číslo s jednou hvězdičkou, takže zabere pevné místo.

### Změněno

- Šířka okna 440 → 500 bodů.

## [0.2.1] - 2026-09-01

### Opraveno

- Panel událostí ukazoval jen tři a půl řádku ze šesti. Návrhová výška okna je nově
  1163 bodů s rozbalenými deskami a 879 se sbalenými.

## [0.2.0] - 2026-09-01

### Přidáno

- Sekci s deskami jde sbalit klikem na nadpis. Když se plné rozložení při startu
  nevejde na obrazovku, sbalí se sama.

### Změněno

- Útok a život mají ikony, tavern tier hvězdičky a klíčová slova se vypisují celým
  jménem.
- Panel událostí drží posledních šest položek místo osmi.

### Opraveno

- `BEGIN_MULLIGAN` se v Battlegrounds překládá jako výběr hrdiny; dosud se ukazovala
  syrová hodnota z logu.

## [0.1.0] - 2026-09-01

### Přidáno

- První vydání. Overlay nad Hearthstone Battlegrounds čte `Power.log` a zobrazuje lobby
  s živým žebříčkem, vlastní desku, nabídku Boba, ruku, zlato, historii soubojů
  a poslední známou desku každého hráče.
- Vydání se staví na tag přes GitHub Actions; aplikace si je sama najde, stáhne a
  nainstaluje při dalším startu.

[Nevydáno]: https://github.com/myspulin24/bg-tracker/compare/v0.14.0...HEAD
[0.14.0]: https://github.com/myspulin24/bg-tracker/compare/v0.13.0...v0.14.0
[0.13.0]: https://github.com/myspulin24/bg-tracker/compare/v0.12.0...v0.13.0
[0.12.0]: https://github.com/myspulin24/bg-tracker/compare/v0.11.0...v0.12.0
[0.11.0]: https://github.com/myspulin24/bg-tracker/compare/v0.10.1...v0.11.0
[0.10.1]: https://github.com/myspulin24/bg-tracker/compare/v0.10.0...v0.10.1
[0.10.0]: https://github.com/myspulin24/bg-tracker/compare/v0.9.3...v0.10.0
[0.9.3]: https://github.com/myspulin24/bg-tracker/compare/v0.9.2...v0.9.3
[0.9.2]: https://github.com/myspulin24/bg-tracker/compare/v0.9.1...v0.9.2
[0.9.1]: https://github.com/myspulin24/bg-tracker/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/myspulin24/bg-tracker/compare/v0.8.1...v0.9.0
[0.8.1]: https://github.com/myspulin24/bg-tracker/compare/v0.8.0...v0.8.1
[0.8.0]: https://github.com/myspulin24/bg-tracker/compare/v0.7.1...v0.8.0
[0.7.1]: https://github.com/myspulin24/bg-tracker/compare/v0.7.0...v0.7.1
[0.7.0]: https://github.com/myspulin24/bg-tracker/compare/v0.6.1...v0.7.0
[0.6.1]: https://github.com/myspulin24/bg-tracker/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/myspulin24/bg-tracker/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/myspulin24/bg-tracker/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/myspulin24/bg-tracker/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/myspulin24/bg-tracker/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/myspulin24/bg-tracker/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/myspulin24/bg-tracker/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/myspulin24/bg-tracker/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/myspulin24/bg-tracker/releases/tag/v0.1.0
