# Développé de coupe de tube → DXF

Appli Windows (WinForms / .NET 8) pour mettre à plat la découpe d'un tube et exporter un **DXF** prêt pour la CAO/CAM (cncKad, etc.).

*Développé avec ♥ par Weapon · pour TolTem*

## Ce que ça fait

- Saisie : Ø extérieur, épaisseur, longueur, angle(s) de coupe.
- Types de coupe : 1 coupe (bout droit), coude (2 coupes en pointe), parallélogramme (2 biais parallèles).
- **Recouvrement de soudure (roulage)** : bande ajoutée dans le sens **circonférentiel** (le développé passe de πØ à πØ + recouvrement), sur **toute la hauteur**, pour pointer la couture après roulage. Le profil se prolonge périodiquement (la matière de recouvrement épouse la surface du tube).
- Aperçu plan en direct + cotes (circonférence, longueurs maxi/mini, largeur développée).
- Export DXF en **mm**, contour fermé sur le calque `DECOUPE`, ligne de fermeture du tube sur le calque `REPERE`, annotation sur `ANNOTATION`.

## Compiler / lancer

Prérequis : **.NET 8 SDK** (Windows).

```bash
cd DeveloppeTube
dotnet run
```

## Générer un .exe autonome (sans .NET installé sur le poste atelier)

```bash
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true
```

L'exécutable se trouve dans :

```
bin\Release\net8.0-windows\win-x64\publish\DeveloppeTube.exe
```

Copie ce seul fichier `.exe` sur la clé USB / le poste atelier — il tourne sans rien installer.

> Version plus légère (si .NET 8 Desktop Runtime est déjà présent) : enlève `--self-contained true` et les deux `-p:`.

## Réglages & préréglages (JSON)

L'appli mémorise automatiquement la **dernière session** (Ø, épaisseur, longueur, angles, couture, recouvrement…) et la restaure au lancement.

Tu peux aussi enregistrer des **préréglages nommés** (section « Préréglages » : *Enregistrer…* / *Charger* / *Suppr.*) — pratique pour tes tubes récurrents.

Tout est stocké dans un fichier JSON lisible/éditable :

```
%AppData%\TolTem\DeveloppeTube\settings.json
```

Exemple :

```json
{
  "Last": { "D": 60, "Th": 2, "L": 150, "AngleA": 45, "Cut": "Single", "Overlap": 20 },
  "Presets": {
    "Tube 60x2": { "D": 60, "Th": 2, "L": 150, "AngleA": 45, "Cut": "Single" }
  }
}
```

## Notes

- L'angle est compté depuis la coupe droite (0° = perpendiculaire). Au-delà de ~89°, le développé tend vers l'infini (coupe parallèle à l'axe).
- « Développer sur Ø moyen » utilise Ø_ext − épaisseur (fibre neutre) ; « Ø extérieur » trace sur la surface.
- Toutes les valeurs numériques du DXF sont écrites en notation point décimal (InvariantCulture), indépendamment des réglages régionaux Windows.
