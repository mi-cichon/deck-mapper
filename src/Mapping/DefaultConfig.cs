namespace DeckMapper;

/// <summary>Domyślny profil (profiles/MTA.json) – zapisywany przy pierwszym uruchomieniu.</summary>
static class DefaultConfig
{
    /// <summary>Ściąga dopisywana na górze każdego profilu (także przy zapisie z edytora).</summary>
    public const string Header = """
// DeckMapper – profil mapowania kontrolera Steam Decka na klawiaturę i mysz.
// Najwygodniej edytować w edytorze (menu ikonki → Edytor mapowania).
// Ręczne zmiany w pliku wczytują się same po zapisaniu.
//
// KONTROLKI:
//   A B X Y  LB RB  LT RT  L3 R3  L4 R4 L5 R5 (tylne)  View Menu Steam QuickAccess ("...")
//   DpadUp DpadDown DpadLeft DpadRight  LeftPadClick RightPadClick
//   LeftPadUp/Down/Left/Right     – lewy touchpad wciśnięty w danym miejscu (dodatkowy d-pad)
//   LeftStickUp/Down/Left/Right   – gdy leftStick.mode = "keys"
//   RightStickUp/Down/Left/Right  – gdy rightStick.mode = "keys"
//   Aliasy: L1 R1 L2 R2 Select Start
//
// AKCJE:
//   "W", "F1", "Space", "LShift", "Enter", "Esc", "Up", "Num5", "Delete"...
//   "Ctrl+F1"                  – kombinacja (wciśnięte razem, puszczane razem)
//   "LMB" "RMB" "MMB" "Mouse4" "Mouse5" "WheelUp" "WheelDown"
//   "Text:/me macha ręką\n"    – wpisuje tekst (\n = Enter)
//   "@Keyboard"                – pokaż/ukryj klawiaturę ekranową
//   "@Pause"                   – wstrzymaj/wznów mapowanie
//   "@Layer:alt"               – trzymaj, żeby działała warstwa "alt"
//   "@Profile:MTA"             – przełącz na profil "MTA"
//   "@NextProfile"             – następny profil (alfabetycznie)
//   "@Editor"                  – otwórz edytor mapowania
//   "@ProfileWheel"            – trzymaj = koło profili (prawa gałka / lewy touchpad), puść = wybór
//   "" albo "none"             – nic (w warstwie: wyłącza przypisanie bazowe)
//
// LIZARD MODE: mysz zostaje sprzętowa (prawy touchpad = kursor, R2 = lewy klik, L2 = prawy klik) –
// tylko taką MTA przyjmuje na serwerze. Klawisze, które lizard sam wysyła (np. Enter pod A),
// połyka filtr: "lizard": { "filterKeys": true, "swallow": "auto" } ("auto" = wszystkie z lizard-map.json,
// albo lista, np. ["Enter", "Esc"]). Akcje myszy (LMB, rightStick.mode = "mouse") w MTA na serwerze nie działają.
//
// KOŁO PROFILI: "wheelLabel": "MTA" (maks. 6 znaków; brak = początek nazwy), "wheelIcon": "🚗" albo "auto.png"
// (plik z profiles/icons/). Sloty koła ustawia się w edytorze (zakładka Koło), zapisują się w settings.json.

""";

    public const string Json = Header + """
{
  "stickDeadzone": 0.35,
  "triggerThreshold": 0.5,
  "leftPadDeadzone": 0.3,

  // filtr klawiszy lizarda: mysz z touchpada działa, A/B/d-pad nie wysyłają Enter/Esc/strzałek
  "lizard": { "filterKeys": true, "swallow": "auto" },

  "leftStick":  { "mode": "keys" },
  "rightStick": { "mode": "mouse", "mouseSpeed": 1200, "curve": 2.0 },

  "bindings": {
    "LeftStickUp": "W", "LeftStickDown": "S", "LeftStickLeft": "A", "LeftStickRight": "D",

    "LB": "Space",          // sprint / ręczny
    "RB": "LShift",         // skok
    "X": "C",               // kucanie
    "Y": "F",               // wsiadanie / wysiadanie
    "L3": "LAlt",           // chód
    "R3": "H",              // klakson

    "L5": "Q",
    "R5": "E",

    "DpadUp": "F1", "DpadRight": "F2", "DpadDown": "F3", "DpadLeft": "F4",

    "View": "Tab",
    "Menu": "T",            // czat
    "Steam": "Esc",

    "L4": "@Layer:alt",     // trzymaj = druga warstwa
    "R4": "@Keyboard",
    "QuickAccess": "@Pause"
  },

  "layers": {
    "alt": {
      "DpadUp": "F5", "DpadRight": "F6", "DpadDown": "F7", "DpadLeft": "F8",
      "X": "1", "Y": "2", "LB": "3", "RB": "4",
      "Menu": "Y",          // czat drużynowy
      "View": "F11",        // mapa
      "QuickAccess": "@Editor"
    }
  }
}

""";
}
