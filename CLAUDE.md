# DeckMapper – notatki dla Claude

Aplikacja WinForms (.NET 8, win-x64) mapująca kontroler Steam Decka na klawiaturę/mysz. Komentarze, logi i UI po polsku.

- Budowanie na macOS: `cd src && dotnet publish -c Release` (działa dzięki `EnableWindowsTargeting`; uruchomić się da tylko
  na Windowsie). Wynik: `src/bin/Release/net8.0-windows/win-x64/publish/DeckMapper.exe`.
- Testy: `cd tests/DeckMapper.Tests && dotnet test` – działają na macOS. Projekt testowy LINKUJE pliki rdzenia ze `src`
  (aplikacja jest `net8.0-windows`, więc zwykła referencja nie wchodzi w grę). Nowy plik rdzenia → dopisz do csproj testów;
  rdzeń nie może używać WinForms ani P/Invoke. `RollForward=Major`, bo na Macu może nie być runtime'u .NET 8.

## Mysz i lizard mode – stan docelowy (branch `feature/lizard-filter`)

**Zasada:** mysz zostaje sprzętowa (lizard mode w całości), a klawisze, które lizard sam wysyła, połyka hook Windowsa.
MTA na serwerze przyjmuje TYLKO sprzętową mysz lizarda – `SendInput` dla myszy (akcje LMB/RMB, `rightStick.mode = "mouse"`)
jest tam odrzucany. Na pulpicie i w menu głównym MTA programowa mysz działa.

### Architektura
- `src/Lizard/` – **rdzeń bez WinForms/Win32, testowany**:
  - `LizardMap.cs` – co lizard emituje dla każdej kontrolki (`LizardOutput`: klawisz jako scancode+extended, sprzętowy
    przycisk myszy albo nic); `lizard-map.json` (obok exe); wbudowana mapa `BuiltIn` (A = Enter, B = Esc, d-pad = strzałki,
    R2/klik prawego padu = LMB, L2 = RMB) oznaczona jako **niezweryfikowana**. Kursor z prawego padu nie jest w mapie.
  - `LizardFilterPlanner.cs` – `Plan(map, profil, globalnyWłącznik)` → `SwallowPlan` (HashSet kodów `scancode | 0x100`
    dla extended), `StatusOf` (plakietki/ostrzeżenia w edytorze), `Toggle` (lista połykanych ↔ "auto"),
    `KeyboardDevices.IsExternalKeyboard` (ścieżki Raw Input; VID_28DE = lizard Decka, pomijany).
  - Profil (`Mapping/Config.cs`): `"lizard": { "filterKeys": true, "swallow": "auto" | ["Enter", …] }` → `LizardProfileSettings`.
    Brak sekcji = filtr włączony, auto. Stara opcja `disableLizardButtons` jest czytana tylko po to, by ją wyzerować
    (`Normalize`) – nie trafia do zapisu i NIC nie robi (na Decku 0x81 zabijał mysz).
- `src/Input/LizardKeyFilter.cs` – WH_KEYBOARD_LL na dedykowanym wątku z pętlą GetMessage. Callback: dwa `ReadInt32`
  + `HashSet.Contains`, bez alokacji i logowania; połyka tylko zdarzenia bez `LLKHF_INJECTED` (nasze SendInput i klawiatura
  ekranowa przechodzą). Mierzy czas callbacku; > 1 ms → ostrzeżenie w logu (zbiorczo co 60 s, razem z licznikiem
  połkniętych). Myszy nie filtruje. `Paused` na czas kreatora.
- `src/Controller/LizardService.cs` (wątek UI) – spina mapę, profil, globalny `settings.json: lizardFilterEnabled`
  i hook; przy zmianie trybu (filtr wł. ↔ wył.) prosi worker o przywrócenie domyślnego lizarda.
- `SteamDeckController.RestoreDefaultLizard` = 0x85 SET_DEFAULT_DIGITAL_MAPPINGS + 0x8E LOAD_DEFAULT_SETTINGS (oba!).
  Worker wysyła to przy każdym połączeniu z kontrolerem (crash / stara wersja z 0x81), na żądanie i przy zamknięciu.
  **Żadnego 0x81 ani komend zapisujących na stałe / factory reset.**
- `src/Input/RawInputDevices.cs` – lista klawiatur (GetRawInputDeviceList); TrayApp co 15 s sprawdza klawiaturę USB/BT
  i raz na uruchomienie pokazuje dymek, gdy filtr jest aktywny.
- UI: `Ui/Editor/LizardWizard.cs` (kreator mapy, hooki LL na wątku UI, połyka sprzętowe klawisze i kliki, zdarzenia
  sprzed zauważenia wciśnięcia liczone z 300 ms „pre-roll”), sekcja „Mysz i lizard” w ustawieniach edytora, plakietki
  `LizardBadge` na liście kontrolek (`EditorParts.cs`), `PickerLizardInfo` w ActionPicker, menu zasobnika
  (filtr wł./wył., kreator), propozycja kreatora przy pierwszym uruchomieniu (`settings.json: lizardWizardOffered`).

## Koło profili (`@ProfileWheel`)

- Rdzeń `src/Wheel/WheelModel.cs` (testowany): `WheelSettings` (settings.json `wheel`: 2–8 slotów, null = pusty,
  martwe strefy gałki 0.5 / padu 0.35), `WheelSlots` (Normalize/Rename/Remove/Resize/Move/Set – zawsze nowa lista),
  `WheelSelector` (sektor 0 u góry, zgodnie z ruchem wskazówek; gałka ma pierwszeństwo przed lewym padem; środek/pusty
  slot = anuluj), `WheelLabels` (maks. 6 znaków widocznych, StringInfo), `WheelItem`.
- Logika w wątku kontrolera: `Mapper` (OpenWheel/ProcessWheel/CancelWheel) – przy otwarciu ReleaseAll(rearm: false),
  potem mapowanie pominięte do puszczenia przycisku koła; SetConfig/SetSuspended przerywają koło. Worker przekazuje
  `WheelSnapshot` (SetWheel). Do UI tylko zdarzenia WheelOpened / WheelSectorChanged / WheelClosed.
- UI: `Ui/WheelOverlay.cs` (WS_EX_LAYERED + UpdateLayeredWindow, NOACTIVATE, TOOLWINDOW, TRANSPARENT; HBITMAP w cache
  per sektor; toast ~1 s), `Ui/WheelRenderer.cs` (wspólny z podglądem `WheelPreview` w edytorze), zakładka „Koło”.
  Sloty zapisuje `ProfileManager.SetWheelSlots` od razu; `wheelLabel`/`wheelIcon` są w profilu (RawConfig) – w zakładce
  Koło edytuje się je dla profilu z ZAZNACZONEGO slotu: profil otwarty w edytorze → kopia robocza (Zapisz), inny → zapis
  od razu do jego pliku. Stuknięcie slotu zaznacza go, ponowne (albo pustego) → wybór profilu.
  Wybór z koła → `Activate(profil, message: null)` (bez dymka).

## Mapper – pułapki
- Ten sam klawisz na kilku kontrolkach (np. W na RT i LeftStickUp): `HeldCounter` – W↓ przy pierwszej, W↑ przy ostatniej.
  Bez tego puszczenie gałki puszczało W trzymane przez RT.
- `ReleaseAll(reason, rearm)`: rearm = klawisze/warstwy wcisną się znowu, jeśli przycisk dalej trzymany (zmiana profilu,
  rozłączenie); akcje jednorazowe (profil, tekst, …) nie są ponawiane. Każde puszczenie z powodem w logu.

## Wyniki PoC (branch `poc/own-mouse`, testy na Decku 2026-11-20)

1. **Własna mysz z SendInput zamiast lizarda – NIE.** MTA po wczytaniu serwera (już panel logowania) ignoruje ruch i kliki
   z SendInput; Steam Input zachowuje się tak samo. Lizard całkowicie wyłączany przez 0x81 + 0x87
   (`SETTING_LEFT/RIGHT_TRACKPAD_MODE` = `TRACKPAD_NONE`).
2. **Lizard bez klawiszy przez 0x81 CLEAR_DIGITAL_MAPPINGS – NIE.** Na Decku 0x81 wyłącza całą mysz lizarda (kursor
   i kliki) razem z klawiszami. Po 0x81 samo 0x85 nie przywraca myszy z padu – potrzebne 0x85 + 0x8E.
3. **Lizard w całości + hook LL połykający jego klawisze – TAK.** Test AHK (`Enter::return`): hook połyka lizardowy Enter
   przed MTA na serwerze, zakładany przed i po starcie MTA. W MTA na serwerze: kamera z touchpada, kliki, WASD z gałki
   działają, Enter pod A połknięty.

### Protokół (źródła: Linux `drivers/hid/hid-steam.c`, SDL `src/joystick/hidapi/steam/controller_constants.h`
i `controller_structs.h`, sc-controller, steamcontroller)
- Feature report: `[report ID 0, cmd, len, dane…]`, ≥ 64 B. ID_*: 0x80 SET_DIGITAL_MAPPINGS, 0x81 CLEAR, 0x82 GET,
  0x85 SET_DEFAULT, 0x86 FACTORY_RESET (nigdy!), 0x87 SET_SETTINGS_VALUES (`[0x87, 3n, (reg, lo, hi)×n]`, tylko RAM),
  0x8E LOAD_DEFAULT_SETTINGS.
- Format 0x80/0x82 (mapa lizarda) **nie jest nigdzie udokumentowany**. SDL ma tylko słownik: `DigitalIO`
  (RT = 0, LT = 1, Y/B/X/A = 2–5, …), `DeviceTypes` (KEYBOARD = 0, MOUSE = 1, …), kody HID klawiszy, `MouseButtons`.
  Plan B (niewdrożony): zrzuty 0x82 → odczytanie formatu → 0x80 z mapą „domyślna minus klawisze”.
- hid-steam.c (master) na Decku wyłącza lizarda przez `SETTING_LIZARD_MODE` (9) = 0 + `SETTING_STEAM_WATCHDOG_ENABLE`
  (71) = 0, bez heartbeatu; wariant z TRACKPAD_NONE jest dla Steam Controllera.

## Kontekst użytkownika
- Gra: MTA (GTA SA) na serwerze RPG bez broni – kliki LMB/RMB z lizarda (R2/L2) w aucie są nieszkodliwe.
