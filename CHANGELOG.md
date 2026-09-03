# Changelog

Všechny podstatné změny v tomto projektu. Formát vychází z
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), číslování se drží
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html).

Protože je hlavní číslo verze nula, jde podle bodu 4 specifikace o počáteční vývoj:
veřejné rozhraní se může měnit i mezi vedlejšími verzemi. Pravidla, kdy se které číslo
zvyšuje, jsou v `documentation.md`, sekce 14.1.

## [Nevydáno]

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

[Nevydáno]: https://github.com/myspulin24/bg-tracker/compare/v0.9.0...HEAD
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
