# 1 Einleitung

## 1.1 Abstract

*Shoot a Bean* ist ein rundenbasiertes Arena-Clash-Spiel für zwei Spieler, das im Stil taktischer Shooter wie *Valorant* spielt, jedoch vollständig auf den Kern der Gefechte reduziert ist: Klassenwahl, Kaufphase, Runde, Auszahlung. Statt eines Objektivs entscheidet allein die Eliminierung des Gegners oder die verbleibende Lebenspunkte-Anzahl bei Ablauf der Rundenuhr.

Die Spielwelt besteht aus einer einzigen Arena, die beiden Spielern auf jedem Rechner in der gleichen Form vorliegt. Eine Partie wird über ein lokales Netzwerk aus zwei Builds heraus gespielt: Ein Rechner hostet, der zweite Build verbindet sich. Es existieren keine Trainingsgegner und keine KI-Gegner — die zweite Spielfigur wird immer von einem Menschen gesteuert. Spielstände und Konfigurationen sind als Unity-ScriptableObject-Assets abgelegt, sodass sich Klassen, Waffen und Spielmodi ohne Codeänderung anpassen lassen.

## 1.2 Aufgabenstellung und Ziel

Ziel des Projekts war die Entwicklung eines kompletten, spielbaren Zwei-Spielerspiels inklusive Menü, Lobby, Netzwerkcode und gefechtsfähigem Gameplay. Der Umfang sollte dabei so gewählt werden, dass jedes Teilsystem — Menü, Lobby, Netzwerk, Match-Logik, Klassen, Waffen, UI — einmal vollständig durchlaufen wird, anstatt mehrere Teilsysteme oberflächlich anzuschneiden.

Abgegrenzt wurde von vornherein auf folgende Bereiche:

- Es wird kein Third-Party-Netzwerk-Framework (NGO, Mirror, Photon) verwendet. Die Netzwerkschicht ist eine Eigenentwicklung auf TCP-Basis.
- Es gibt keine KI-Gegner, keine Einzelspielerkampagne und keinen Schwierigkeitsgrad.
- Es wird kein Online-Matchmaking über das Internet angeboten. Partien finden im lokalen Netzwerk statt.
- Es werden keine Musik und keine Sprachausgabe produziert.

## 1.3 Zielgruppenanalyse

Die Zielgruppe des Spiels sind Spieler, die an taktischen Rundenspielern interessiert sind und eine kurze Partie in direkter Umgebung mit einem zweiten Spieler spielen möchten. Das Spiel richtet sich an Personen, die den Karten- und Klassenaspekt eines *Valorant* schätzen, aber den Einstieg über eine einzelne, überschaubare Karte und eine reduzierte Waffenauswahl suchen.

Daraus ergeben sich für die Umsetzung folgende Anforderungen:

- Die Bedienung muss ohne Erklärung verständlich sein. Jedes Menü erklärt seine Funktionen zur Laufzeit.
- Die Partie muss innerhalb weniger Minuten spielbar sein. Menü, Lobby und Match bilden einen kurzen, durchgehenden Pfad.
- Die Klassen und Waffen müssen sich in ihren Werten spürbar voneinander unterscheiden, damit die Wahl zu Beginn einer Partie eine taktische Entscheidung darstellt.
- Das Spiel muss lokal ohne Konfiguration spielbar sein. Es fallen keine Konten, Passwörter oder externe Server an.

# 2 Hauptteil

## 2.1 Software / Hardware

### 2.1.1 Einrichten der Entwicklungsumgebung

**Grundvoraussetzungen.** Das Spiel wurde mit Unity 6000.3.10f1 entwickelt. Als grafische Schnittstelle kommt das Universal Render Pipeline (URP) 17.3.0 zum Einsatz, als Steuereingabe das neue Unity Input System 1.18.0. Die Skripte sind in C# verfasst. Für die Bearbeitung der Assets werden der Unity Editor und eine IDE (Visual Studio oder JetBrains Rider) benötigt. Zum Bau der ausführbaren Datei wird ein Windows-Rechner mit DirectX 12 benötigt.

**Installation.** Nach dem Klonen des Projektordners wird dieser in Unity Hub als vorhandenes Projekt hinzugefügt. Der Editor importiert die Assets automatisch. Anschließend kann das Spiel direkt im Editor gestartet werden oder über *File / Build Settings* ein eigenständiger Build erstellt werden. Weitere Konfigurationen fallen nicht an.

**Ausführen.** Der Build startet im Hauptmenü. PLAY öffnet die Lobby. Dort wird entweder eine Partie erstellt oder einer im lokalen Netzwerk gefundenen Partie beigetreten. Der Host startet die Partie mit dem Startbutton. Eine zweite Instanz des Builds auf einem anderen Rechner im gleichen Netzwerk verbindet sich über die Lobbyliste. Der Host kann auch die IP-Adresse des Clients manuell eintragen.

### 2.1.2 Produktbeschreibung

**Ist-Situation.** Grundlage war eine frühere Prototyp-Szene mit menschlichen Spielfiguren und einer unvollständigen Gefechtslogik. Das Spiel war in dieser Form nicht spielbar: Das Menü war nicht an den eigentlichen Spielablauf gekoppelt, es fehlten Lobby und Netzwerk, und die Figur war nicht bewaffnet.

**Soll-Situation.** Angestrebt wurde ein vollständig durchlaufbares Spiel: Menü mit Einstellungen, Lobby mit Chat, LAN-Partie, Klassenwahl, Kaufphase, Runde, Punktevergabe, Matchende. Als Spielfiguren kommen die im Projekt selbst modellierten Bean-Charaktere zum Einsatz.

**Funktionsumfang.**

*Must-have*

- Hauptmenü mit PLAY, SETTINGS und QUIT
- Lobby mit Erstellen, Beitreten und Chat
- LAN-Suche und manueller Verbindungsversuch
- Rundenbasiertes Match mit Kaufphase und Auszahlung
- 29 Klassen und 18 Waffen
- Klassen- und Waffenauswahl im laufenden Spiel
- HUD mit Lebenspunkten, Munition, Fähigkeiten und Tabelle

*Nice-to-have*

- Titelbild im Hauptmenü
- Vollständig animierte Charaktere
- Nachträglich ergänzte Waffenmodelle
- Anpassbare Eingabebelegung

**Projektabgrenzung.** Nicht abgedeckt werden: Serversuche über das Internet, Matchmaking nach Fertigkeitsgrad, Rangliste, Voice-Chat, Tonausgabe, Trainingsmodus gegen einen computergesteuerten Gegner.

### 2.1.3 Model

Das gesamte Spiel ist in Unity als eine Sammlung von Einzelkomponenten aufgebaut. Es ist keine klassische Datenbank im Spiel enthalten. Die persistenten Daten fallen in drei Gruppen.

**Spieldaten** sind Unity-ScriptableObjects. Eine Klasse ist ein Asset mit Anzeigenamen, Rolle, Beschreibung, Eigenschaften, negativer Eigenschaft, maximaler Lebenspunkte-Zahl und ihrer Fähigkeitenliste. Eine Waffe ist ein Asset mit Schaden, Reichweite, Magazin, Kadenz, Streuung, Kopf- und Beinschuss-Multiplikatoren und Kaufpreis. Ein Spielmodus (Preset) ist ein Asset, das Rundenzahl, Zeitvorgaben, Startgeld, Prämien und Aktivierung von Cheat-Optionen zusammenfasst. Alle Werte lassen sich im Unity Editor bearbeiten, ohne Code anzufassen. Tabelle 1 zeigt die Verteilung.

*Tabelle 1: Bestandteile der Spieldaten.*

| Bestand | Anzahl | Ort im Projekt |
| --- | --- | --- |
| Klassen | 29 | `Assets/Data/Manuel/Classes` |
| Waffen | 18 | `Assets/Data/Manuel/Weapons` |
| Spielmodi | 1 | `Assets/Resources/Manuel/Presets` |

**Eingabebelegung** wird als JSON-Zeichenkette in den Spieler-Einstellungen abgelegt und beim Start geladen. Im Einstellungsmenü kann jede Aktion neu belegt und gespeichert werden.

**Netzwerkdaten** bestehen aus dem Zustand der Partie. Sie werden nicht gespeichert, sondern nur für die Dauer der Partie zwischen beiden Rechnern ausgetauscht. Jede Nachricht trägt einen Typbezeichner, an dem sie erkannt wird.

### 2.1.4 View (GUI)

Jede Benutzeroberfläche des Spiels wird zur Laufzeit aus Code aufgebaut. Es werden keine vorgefertigten UI-Prefabs verwendet. Grund dafür ist bewusst: Alle Bedienelemente werden durch Positionsabfragen getroffen, wofür Unity eine eventbasierte UI nicht zwingend voraussetzt.

Folgende Bildschirme existieren in dieser Reihenfolge:

1. **Hauptmenü.** PLAY, SETTINGS und QUIT. Der Mittelteil des Menüs zeigt das Titelbild des Spiels, keine Textüberschrift.
2. **Einstellungsmenü.** Register für Steuerung, Spiel, Anzeige und Ton. Der Reiter Steuerung erlaubt das Neubelegen jeder Aktion.
3. **Lobby.** Nach PLAY. Auswahl zwischen Partie erstellen und Partie beitreten. Beitreten listet alle im Netzwerk gefundenen Partien und bietet eine manuelle Adresseingabe.
4. **Lobby-Raum.** Namen beider Spieler, Chat und der Startbutton, den nur der Host drücken kann.
5. **Klassenwahl.** Die Klassen in Gruppen nach Rolle. Nur der Klassenname wird gezeigt, Fähigkeiten und Werte nicht.
6. **Kaufphase / Waffen.** Aufgeteilt: Klassenliste links, kaufbare Waffen rechts. Waffen können nur in dieser Phase gekauft werden.
7. **Match-HUD.** Phasenname und Uhr oben, Lebenspunkte, Fähigkeiten und Waffenstatus unten, Tabelle auf Tastendruck.

Die Spiel-Szenen selbst sind in Unity als Szenen gebaut. Der Wechsel zwischen Menü und Match erfolgt über den Szenenwechsel, den die Lobby auslöst. Tabelle 2 listet die Szenen.

*Tabelle 2: Szenen des Spiels.*

| Szene | Inhalt |
| --- | --- |
| `MainMenu` | Hauptmenü und Einstellungen |
| `Manuel` | Arena, Spieler und Match-Logik |

### 2.1.5 Control

**Produktfunktionen.** Die Spiellogik ist in eigenständige Komponenten aufgeteilt. Jede Komponente ist für einen abgegrenzten Bereich zuständig und kommuniziert mit den anderen über Methodenaufrufe und Ereignisse.

*Tabelle 3: Die Komponenten der Spiellogik.*

| Komponente | Aufgabe |
| --- | --- |
| GameManager | Phasen, Timer und Rundenverlauf |
| RoundManager | Rundenzähler und Respawnpunkte |
| ScoreManager | Punkte, Eliminierungen und Spielausgang |
| EconomyManager | Geld, Prämien und Rundenauszahlung |
| WeaponManager | Waffenbesitz und -auswahl |
| WeaponUser | Feuern, Nachladen, Treffer |
| ClassManager | Klassenverzeichnis und -auswahl |
| ClassAbilityHost | Fähigkeiten einer gewählten Klasse |
| Health | Lebenspunkte und Schild einer Figur |
| SpawnManager | Spawn- und Respawnpunkte |
| LobbyNetwork | Verbindungsaufbau und Nachrichten |
| NetMatchSync | Zustandssynchronisation beider Rechner |

**Anwendungsfall: Eine Partie spielen.** Der Spieler wählt im Hauptmenü PLAY, entscheidet sich in der Lobby für Erstellen und wartet, bis sich ein zweiter Spieler verbindet. Der Host startet die Partie. Beide wählen eine Klasse, beide kaufen Waffen. Die Runde beginnt. Wer den Gegner eliminiert, gewinnt die Runde und erhält eine Prämie. Wer zuerst die eingestellte Rundenzahl erreicht, gewinnt das Match. Nach dem Match kehrt die Lobby zurück, in der eine neue Partie gestartet werden kann.

**Anwendungsfall: Einem laufenden Match beitreten.** Der Spieler wählt PLAY, dann Beitreten. Die Lobby zeigt alle im lokalen Netzwerk sichtbaren Partien. Der Spieler wählt eine aus. Die Verbindung steht, sobald der Host bestätigt. Beide sehen sich im Lobby-Raum.

### 2.1.6 Problemlösungen

**Waffenmodelle erschienen verkehrt herum.** Die Modelle wurden aus Blender exportiert und in Unity importiert. In der Ansicht war das Modell gedreht und die Waffe befand sich nicht im Sichtfeld. Ursache war eine fehlende Socket-Zuweisung: Ohne einen zugewiesenen Socket fiel das Modell auf den Wurzel-Knoten der Spielfigur zurück und nahm dessen Position ein, wodurch es weder den Kopf noch die Blickrichtung mitdrehte. Behoben wurde dies durch einen separaten Socket-Knoten als Kind der Kamera, dem das Modell zugewiesen wird.

**Lebenspunkte und Schussfunktion waren nicht sichtbar.** Die Kamera war nicht als *MainCamera* markiert. Das Spiel nutzt `Camera.main` zur Auflösung des Ursprungs für Schüsse und Sichtlinien. Ohne die Markierung ist diese Referenz leer, und der Aufruf einmal in `Awake` bleibt für immer leer. Die Auswirkung war, dass jeder Schuss stumm fehlschlug. Behoben wurde die Markierung, zusätzlich wird die Referenz nun bei jedem Schuss frisch aufgelöst, damit ein Client, dessen Kamera erst nach `Awake` entsteht, trotzdem feuern kann.

**Charaktergröße war doppelt so groß.** Die Bean-Modelle wurden in Blender vermessen, um den Scale-Faktor zu berechnen. Der erste Versuch nutzte die localBounds, die sich in den Achsen des Modells selbst befinden. Bei einem Blender-Export liegt die Höhe des Modells nicht auf Y, sondern auf Z. Die Berechnung teilte dadurch durch die Tiefe des Modells statt durch seine Höhe. Der Scale-Faktor wurde doppelt so groß und die Figur dadurch doppelt so hoch. Behoben wurde dies durch die Verwendung der Welt-Bounds, die das Spiel tatsächlich zeichnet, in denen die Höhe zuverlässig auf Y liegt.

**Charaktere schwebten über dem Boden.** Nachdem die Größe stimmte, stand die Figur einen halben Meter über dem Boden. Die verwendete Unterkante war nicht die tatsächlich gezeigte Unterkante des Modells, da die Figur ihre eigene Laufanimation abspielt und sich dabei streckt. Behoben wurde dies durch die Nutzung der gezeichneten Unterkante anstelle der Modell-Unterkante.

**Rundenstart erfolgte ohne Vorlauf.** Nach der Klassenwahl begann die Runde unmittelbar. Den Spielern fehlte die Zeit, die Waffenauswahl zu treffen. Behoben wurde dies durch eine eigene Kaufphase, die als fester Phasenabschnitt zwischen Klassenwahl und Runde liegt und mit einer Uhr herunterzählt.

**Kauf von Waffen schlug fehl.** Im Waffenmenü konnte keine Waffe erworben werden. Ursache war, dass das Menü die Waffenliste und die Kauflogik voneinander getrennt aufgebaut hatte, ohne dass die Liste den Kaufzustand abfragte. Behoben wurde dies, indem die Liste dieselbe Kaufprüfung verwendet wie die Kauflogik und nur kauft, wenn Geld und Besitz es zulassen.

**Fehlende Übersicht im laufenden Spiel.** Im Match waren weder Lebenspunkte noch ein Fadenkreuz zu sehen. Es war zudem unklar, wann eine Fähigkeit wieder einsetzbar ist. Ergänzt wurden ein Fadenkreuz in der Bildschirmmitte, eine Lebenspunkte-Anzeige unten links mit Balken und Zahl sowie eine Fähigkeitenliste, die Cooldown und Bereitschaft je Fähigkeit zeigt.

**Texte im Hauptmenü.** Das Hauptmenü zeigte Texte des Match-HUD an, obwohl kein Match lief. Ursache war, dass der Match-HUD auf jedem Szenenwechsel installiert wurde, auch in der Menüszene. Behoben wurde dies, indem die Installation an das Vorhandensein einer Spielerfigur in der Szene gebunden wurde.

### 2.1.7 Hardware

Das Spiel ist ein reines Softwareprojekt. Es werden keine Hardwarekomponenten angesteuert, und es fallen keine Schaltpläne oder Pin-Belegungen an. Als Entwicklungs- und Spielrechner kamen handelsübliche Windows-PCs mit DirectX-12-Grafikkarte zum Einsatz. Für die Zweispielersitzung wurden zwei Rechner im selben lokalen Netzwerk verwendet.

## 2.2 Medienprojekt

### 2.2.1 Konkurrenzanalyse

**Valorant** (Riot Games). Taktischer Shooter mit Klassen, Kaufphase und Rundensystem. Es ist das Spiel, an dem sich dieses Projekt orientiert. Besonderheit: sehr große Karten, viele Klassen, E-Sport-Ausrichtung, Internet-Server und Matchmaking.

**Counter-Strike 2** (Valve). Taktischer Shooter ohne Klassen, mit Waffenkauf und Runden. Besonderheit: Fokus auf Waffenbeherrschung, keine Fähigkeiten, Internet-Server.

Absetzung des eigenen Spiels: *Shoot a Bean* bietet den gleichen Phasenablauf, reduziert ihn aber auf eine Karte, zwei Spieler und eine lokale Partie. Die Klassen sind Eigenschaften-Assets, die Waffen sind Wertetabellen. Es ist in wenigen Minuten verständlich und ohne Internet spielbar. Auf E-Sport-Funktionen, Rangliste und Internet-Server wird bewusst verzichtet.

### 2.2.2 Ideenfindung und Kreativprozess

Die Idee entstand aus dem Wunsch, den Phasenablauf eines taktischen Shooters in einem kleinen, durchspielbaren Rahmen nachzubauen. Die Bean-Charaktere entstanden als eigene Modelle in Blender und gaben dem Spiel seinen Namen. Die Klassen und Waffen wurden als Wertetabellen entworfen, damit sie sich ohne Codeänderung anpassen und ausbalancieren lassen.

## 2.3 Verlauf des Projekts

Die Arbeit wurde zwischen Emmanuel Johnson und Tahsin Can Gördesli aufgeteilt. Emmanuel Johnson trug den Hauptteil: Menü, Lobby, Netzwerk, Match-Logik, Klassen- und Waffensystem, das Spielfeld und die Charaktere. Tahsin Can Gördesli übernahm unterstützende Aufgaben in den Bereichen Waffenwerte, Klassenbeschreibungen, HUD und Test der Zweispielersitzung.

Die Arbeitsschritte im Überblick:

1. Grundszene, Spielerfigur und Bewegung
2. Menü und Einstellungen
3. Lobby und Chat
4. Netzwerk für zwei Spieler
5. Klassen und Fähigkeiten
6. Waffen und Waffenmodelle
7. Match-Logik, Phasen und Punkte
8. HUD und Titelbild
9. Auslieferung und Dokumentation

## 2.4 Zusammenfassung und Ausblick

Das Ergebnis ist ein vollständig spielbares Zwei-Spielerspiel. Der beschriebene Phasenablauf läuft von Menü bis Matchende ohne Unterbrechung durch. Die Klassen und Waffen unterscheiden sich spürbar in ihren Werten. Das Spiel ist im lokalen Netzwerk mit zwei Builds spielbar.

Als sinnvolle nächste Schritte bieten sich an:

- Ergänzung weiterer Spielmodi und Karten
- Erweiterung der Cheat-Optionen zu einstellbaren Spielregeln
- Nachträgliches Hinzufügen von Tonausgabe
- Optionale Vernetzung über das Internet

## 2.5 Literaturverzeichnis

- Blender Foundation (2026): *Blender 5.1*. https://www.blender.org (Stand: 25.09.2026)
- Riot Games (2026): *Valorant*. https://playvalorant.com (Stand: 25.09.2026)
- Unity Technologies (2026): *Unity 6000.3.10f1 — Universal Render Pipeline 17.3.0, Input System 1.18.0*. https://unity.com (Stand: 25.09.2026)
- Unity Technologies (2026): *Unity-Dokumentation — Skript- und Editor-API*. https://docs.unity3d.com (Stand: 25.09.2026)
- Valve (2026): *Counter-Strike 2*. https://store.steampowered.com/app/730 (Stand: 25.09.2026)

# 3 Erklärung

Hiermit wird erklärt, dass die vorliegende Dokumentation und das beschriebene Spiel von Emmanuel Johnson und Tahsin Can Gördesli selbstständig und ohne unerlaubte Hilfe erstellt wurden. Sämtliche Quellen sind im Text und im Literaturverzeichnis angegeben.

Paderborn, den 25. September 2026

Emmanuel Johnson

Tahsin Can Gördesli
