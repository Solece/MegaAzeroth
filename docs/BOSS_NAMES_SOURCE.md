# Source-bound boss display names

The HUD labels follow the source model actually attached to a native boss. This change does not replace models, textures, native boss identities, AI, health, spawning, rewards or collision. The labels are generic creature/model-family names and never assert that a current model is a named original Azeroth boss.

Source version: Azeroth Survivors0.102.3 (`f78e6fab`), executable SHA256 `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.

| Actual model key | Source DisplayID | English HUD label | Russian HUD label | Name authority |
| --- | ---: | --- | --- | --- |
| ogre | 415 | Ogre | Огр | Shortened family label; this exact yellow appearance is source enemy804 Gordunni Ogre and enemy304 Crushridge Brute. |
| infernal | 169 | Infernal | Инфернал | Exact original localized creature name, enemy107. |
| tarantula | 520 | Tarantula | Тарантул | Original model-family name plus explicit Russian translation. This brown display is absent from the source actor registries. |
| lich | 7919 | Lich | Лич | Original model-family name plus explicit Russian translation. No original localized actor identity is claimed. |
| bonegolem | 12073 | Bone Golem | Костяной голем | Original model-family name plus explicit Russian translation. No original localized actor identity is claimed. |
| doomguard | 433 | Doomguard | Стражник ужаса | Exact original localized creature name, enemy111. |
| dragonspawncaster | 6761 | Dragonspawn Caster | Драконид-заклинатель | Original model-family name plus explicit Russian translation. This blue appearance is absent from the source actor registries. |

For the four unregistered model families: Russian model-family translation selected by the integration; not an original localized actor name. For Ogre, source enemy804 is `Gordunni Ogre` / `Огр Гордунни`; source enemy304 is `Crushridge Brute` / `Громила из клана Тяжелого Кряжа`. A shorter family label avoids arbitrarily assigning either ordinary-creature identity to every native boss using the yellow Ogre model.

## Verified extraction

The original zone table starts at raw offset `0x6b84480`, with12 records of240 bytes. The enemy array pointer/count fields are at zone+0x78/+0x80; boss array pointer/count fields are at+0x88/+0x90. Eleven zones have12 ordinary enemy records and Naxxramas has16:148 records total. All12 zones have five bosses:60 boss records.

An ordinary enemy record is80 bytes; name pointer/length are+0x10/+0x18, source enemy ID+0x20 and display ID+0x24. A boss record is88 bytes; name pointer/length are+0/+8, source boss ID+0x30 and display ID+0x34. Every extracted ID resolves to its embedded English/Russian localization key. English JSON begins at raw `0x6b9bc92`; Russian JSON begins at `0x6d2cf6b`.

The active v6 asset model display IDs, original M2 hashes and used skin BLP hashes were checked against the owned original archives. CreatureDisplayInfo.dbc and CreatureModelData.dbc resolve the actual original model and skin triples. None of the60 original bosses has the exact model-and-skin appearance of any of the seven current boss model keys. Exact ordinary-creature matches are reported separately above.

## Important rejected identities

- Yellow Ogre415 is not Rhahk'Zor14403 (`OgreSkinBeige`) or Kovork448 (`OgreSkinGray`). King Gordok uses a different OgreWarlord M2.
- Brown Tarantula520 is not Naraxis1091 (`TarantulaSkinGrey`). Mother Fang7536 uses `MineSpiderBoss.mdx`, a different model.
- Red Doomguard433 is not Doomguard Commander68 (blue skin/wing). Lord Kazzak12449 uses a different LordKezzak M2.
- Lich7919 is not Kel'Thuzad15945, which uses `Creature\KelThuzad\KelThuzad.mdx`.
- BoneGolem12073 is not Patchwerk16174, which uses a FleshGolem M2.
- Blue DragonspawnCaster6761 is not Black Wyrmkin8249 (black skin/body).

The machine-readable `boss-name-source-contract.json` contains exact source record locations, source display/model/skin rows, hashes, matching ordinary-creature records and rejected same-model/different-skin boss candidates. It contains metadata only; no original executable, archive or asset binary is included.

Runtime catalog schema is `format: azeroth-boss-names-v1`, `entries: [{modelKey,ru,en,sourceDisplayId,evidence}]`. Evidence explicitly records the name classification and `namedBossIdentity:false`. Unknown/unattached model keys must retain the native name; fallback should not silently assign an Ogre name. Binding uses the actual visual model key, so native enum aliases cannot supply a stale label.

The source audit is read only. Native HUD binding and scene-lifecycle tests are performed separately by the integrating module.
