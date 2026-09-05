# Hearthstone Battlegrounds Tracker

Lokální overlay pro Hearthstone Battlegrounds. Čte pouze `Power.log`, nijak
nezasahuje do procesu hry a neautomatizuje vstupy.

Změny po verzích jsou v [CHANGELOG.md](CHANGELOG.md). Číslování se drží
[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html); pravidla a postup vydání
popisuje `documentation.md`, sekce 14.

## Instalace

1. Stáhněte `BattlegroundsTracker.exe` z [posledního vydání](https://github.com/myspulin24/bg-tracker/releases/latest).
2. Uložte ho někam, kam máte právo zápisu, například `C:\Users\<vy>\BGTracker\`.
   **Ne do `Program Files`** — tam by se nemohly instalovat aktualizace.
3. Spusťte ho. Windows u staženého nepodepsaného programu ukáže „Windows protected
   your PC“; klikněte na **More info** a **Run anyway**.
4. Nic dalšího instalovat nemusíte, `.exe` je soběstačný.

Hearthstone přepněte na windowed nebo borderless fullscreen, jinak se overlay nad hrou
nemusí zobrazit.

### Aktualizace

Tracker si po startu sám ověří, jestli nevyšla novější verze, stáhne ji na pozadí
a nainstaluje při dalším spuštění. Rozehraný zápas tím nikdy nepřeruší. Aktuální verzi
najdete v patičce okna.

## Vývoj

Repozitář obsahuje desktopový WPF overlay, konzolovou variantu pro diagnostiku
a sdílenou knihovnu s parserem.

## Desktopové demo

Spuštění normálního okna bez konzolového UI:

```powershell
dotnet run --project src/Tracker.Desktop
```

Demo používá syntetická data, samo přehrává průběh zápasu a lze ho pozastavit nebo
restartovat. Okno je vždy na popředí, lze ho přetahovat za horní lištu, měnit jeho
velikost, sbalit na malou lištu nebo ukončit tlačítkem `×`. Pro zobrazení nad hrou
použijte v Hearthstonu windowed/borderless fullscreen; běžné WPF okno se nad
exkluzivním fullscreenem nemusí zobrazit. Samostatné GUI `.exe` vytvoříte příkazem:

```powershell
.\scripts\publish-gui.ps1
```

Výsledek je v `artifacts\desktop\win-x64\BattlegroundsTracker.exe`.

Při startu GUI zobrazí prázdný režim **NASLOUCHÁM** a čeká na aktuální relaci
Hearthstonu a její nový `Power.log`. Staré logy z minulých relací automaticky
nenačítá. Demo se spustí pouze tlačítkem **Spustit demo**; tlačítkem **Vybrat log**
lze soubor zvolit ručně. Cestu lze předat také parametrem:

```powershell
dotnet run --project src/Tracker.Desktop -- --log "D:\Hearthstone\Logs\Power.log"
```

Tracker ponechává zdrojový `Power.log` beze změny. Každý zápas archivuje zvlášť do
`%LOCALAPPDATA%\BattlegroundsTracker\matches` a poslední přečtenou pozici ukládá do
`%LOCALAPPDATA%\BattlegroundsTracker\checkpoint.json`. Pokud je při ukončení
trackeru hra rozehraná, při příštím spuštění se obnoví ze stejného souboru. Po
`FINAL_GAMEOVER` se soubor uzavře a další zápas dostane nový soubor.

Pilot umí:

- sledovat rostoucí `Power.log` i jeho nahrazení nebo zkrácení;
- ukládat každý zápas do samostatného souboru a po restartu obnovit rozehranou hru;
- rozpoznat začátek hry, Battlegrounds kolo a fáze recruit/combat;
- ukázat celé osmičlenné lobby: hrdinu, BattleTag, HP, armor, Tavern Tier, počet triplů,
  živé pořadí v žebříčku a vyřazené hráče;
- ukázat vlastní desku, desku aktuálního soupeře, nabídku Boba a karty v ruce
  se statistikami, tierem, zlatou verzí a klíčovými slovy;
- po najetí myší na hráče ukázat jeho poslední známou desku i s číslem kola;
- v Duos seskupit lobby po dvojicích se sdílenými životy, ukázat desku spoluhráče i obou
  soupeřů ze souboje, kdo z dvojice bojuje první, nápovědu páru či triplu pro spoluhráče
  u karet v nabídce a ohlásit kartu předanou spoluhráči;
- vypsat typy minionů, které se v lobby objevily v nabídce Boba;
- otevřít patch notes Hearthstonu ve vestavěném prohlížeči, v okně nad hrou;
- držet počítadlo bonusů platných pro celou hru: o kolik víc dávají tavern kouzla
  a blood gemy a jaký plošný buff mají elementálové a piráti;
- sledovat zlato, cenu upgradu tavernu, dalšího soupeře a historii soubojů;
- zobrazit poslední významné události a diagnostiku parseru;
- vést historii posledních zápasů zvlášť pro sólo a Duos: hrdina, umístění, změna MMR
  a zůstatek MMR. Rating hra do logů nepíše, takže se zůstatek po zápase opíše do řádku
  a tracker spočítá změnu i aktuální MMR;
- přehrát uložený log nebo vestavěnou syntetickou ukázku; počet uložených zápasů je
  v nastavení (1 až 200);
- přizpůsobit se v nastavení: tmavý nebo světlý motiv, šest akcentů, krytí a zvětšení okna,
  zapínání jednotlivých sekcí, umístění detailů vpravo nebo dole, počet událostí, hustota
  lobby, vždy navrchu, pamatování polohy a instalace hry mimo obvyklé cesty. Nastavení se
  ukládá do `%LOCALAPPDATA%\BattlegroundsTracker\settings.json`; složku dat přesměruje
  proměnná prostředí `BGTRACKER_DATA_DIR`.

`Power.log` není stabilní veřejné API. Skutečný formát se může mezi verzemi hry
měnit a pilot záměrně netvrdí, že pozorovaná entita je vždy lokální hráč.

## Požadavky

- Windows;
- .NET 8 SDK nebo novější pro vývojové spuštění;
- Visual Studio 2022 s workloadem **.NET desktop development** je volitelné;
- runtime WebView2 jen pro okno s patch notes. Na Windows 11 a všude s Edge je
  předinstalovaný; když chybí, odkaz se otevře v systémovém prohlížeči a nic jiného se
  nemění.

## První spuštění

Nejprve ověřte aplikaci nad přiloženými daty:

```powershell
dotnet run --project src/Tracker.App -- --demo
```

Pro živé sledování spusťte:

```powershell
dotnet run --project src/Tracker.App
```

Aplikace hledá `Power.log` v obvyklých instalačních adresářích. Vlastní cestu lze
zadat explicitně:

```powershell
dotnet run --project src/Tracker.App -- --log "D:\Hearthstone\Logs\Power.log"
```

Uložený log lze pouze načíst a ukončit aplikaci:

```powershell
dotnet run --project src/Tracker.App -- --replay --log ".\Power.log"
```

## Zapnutí Power.log

Pokud Hearthstone `Power.log` nevytváří, ukončete hru a vytvořte nebo doplňte
soubor `%LOCALAPPDATA%\Blizzard\Hearthstone\log.config`:

```ini
[Power]
LogLevel=1
FilePrinting=true
ConsolePrinting=false
ScreenPrinting=false
Verbose=true
```

Potom Hearthstone znovu spusťte. Umístění logu závisí na instalaci; často je v
adresáři `Logs` uvnitř instalace Hearthstonu. Existující `log.config` před změnou
zálohujte a zachovejte jeho ostatní sekce.

## Visual Studio

Otevřete `HearthstoneBattlegroundsTracker.sln`, nastavte `Tracker.App` jako startup
projekt a použijte `F5` nebo `Ctrl+F5`. Parametry lze nastavit ve vlastnostech
debug profilu projektu.

## Testy

```powershell
dotnet test
```

## Samostatné EXE

```powershell
.\scripts\publish.ps1
```

Výsledek bude v `artifacts\publish\win-x64`. Jde o self-contained single-file
aplikaci, takže cílový počítač nepotřebuje samostatně instalovaný .NET runtime.

## Aktuální omezení pilotu

- nerozlišuje se stoprocentní jistotou lokálního hráče od soupeřů;
- nerekonstruuje zatím board ani miniony v ruce;
- identifikátory karet zatím nepřekládá na lokalizované názvy;
- po patchi Hearthstonu může být nutná úprava parseru.
