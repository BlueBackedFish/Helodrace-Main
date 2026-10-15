# 탄약 피해량·관통력 차트

2026-10-15 기준. 사용자 지정 수치를 탄종별 공통 기준으로 적용한다. 피해량은 투사체 한 발 기준이며, 산탄은 **한 알 기준**이다. 관통력은 XML의 `armorPenetrationBase`를 백분율로 표시한다. 28%는 `0.28`, 180%는 `1.80`이다. 관통력은 명중률이나 고정 방어 무시 확률을 뜻하지 않는다.

| 탄종 | 피해량 | 관통력 | 적용 무기·용도 |
|---|---:|---:|---|
| 9×19mm M882 | 10 | 14% | Glock 19, P320, MP5 계열 |
| .45 ACP M1911 Ball | 12 | 12% | M1911, Thompson, M3 계열 |
| .30 Carbine M1 Ball | 13 | 15% | M1A1·M2 Carbine |
| .300 BLK TAC-TX | 15 | 19% | .300 BLK 탄종을 선택한 모듈식 소총 |
| 5.56mm M855A1 | 13 | 28% | M16·M4·MK18·M249 계열의 공용 5.56 투사체 |
| 7.62×51mm M80A1 | 18 | 34% | M14·M14 EBR 계열 |
| .30-06 M2 Ball | 19 | 27% | M1903, M1 Garand, BAR, M1917, M1919A6 |
| 12.7mm M1 Ball | 32 | 42% | M2HB, 항공지원 .50구경 |
| M6A3 HEAT | 45 | 90% | 바주카 성형작약 로켓 |
| BGM-71A TOW | 100 | 180% | TOW 유도 미사일 |
| 20mm M53 | 36 | 58% | M167 철갑소이탄 |
| 20mm M56A3 | 30 | 42% | M167 고폭소이예광탄; 비예광 고폭소이탄도 같은 기준 |
| 12게이지 00 Buckshot | 5 | 8% | M1887·M1897, 9알 발사 |
| 40mm M576 MP | 2 | 3% | M79·M203A2, 20알 발사 |

## 밸런스 해석

- 9mm는 .45 ACP보다 피해가 낮고 관통력이 조금 높다. 권총·기관단총 차이는 발사 속도, 조준시간, 정확도와 탄창으로 조정한다.
- .30 Carbine과 5.56mm는 피해 13으로 같고, 5.56mm의 관통력을 높여 방어구 대응 능력을 구분한다. .300 BLK는 5.56mm보다 피해·저지력이 높고 관통력·탄속이 낮다.
- M80A1은 .30-06보다 피해가 1 낮고 관통력이 7%p 높다. 같은 .30-06을 쓰는 소총과 기관총은 한 발 피해를 통일하고 연사·명중률로 구분한다.
- 12게이지의 감쇠 전 피해 합계는 `5 × 9 = 45`, M576은 `2 × 20 = 40`이다. 전탄 명중을 가정한 합계이며, 탄별 관통력을 합산하지 않는다. 거리별 에너지 감쇠·산포·장갑 판정 때문에 실제 피해는 달라진다.
- M53은 철갑, M56A3는 폭발·파편·발화 역할이다. M56A3의 30은 폭발 기본 피해이며 별도로 생성되는 파편과 화상 피해를 포함하지 않는다.
- M6A3와 TOW의 수치는 폭발 기본 피해·관통력이다. 폭발 반경은 각각 2.0칸·1.2칸이며, 표의 피해량이 단일 표적에 보장되는 값은 아니다.

이번 조정은 피해량·관통력에 한정한다. 연사속도, 조준시간, 정확도, 사거리, 저지력, 탄속, 탄창, 산탄 수·산포·감쇠, 폭발 반경, 파편 및 발화 설정은 기존 값을 유지한다. 총기별 최종 밸런스는 게임에서 비장갑·방탄복·중장갑 표적에 대한 제압 시간과 탄약 소모량을 비교해 조정한다.

## XML 적용 위치

방탄판의 확정 방어 컷은 SAPI·SSAPI 27%, ESAPI·ESBI·RAMPART 4800 34%로 맞춘다. 해당 방향 판의 내구도 40% 이상에서 적용하며, 컷을 넘는 탄은 일반 방어 확률로 판정한다. [방탄판 대응표](Ballistic_Reference_Notes.md)를 참고한다.

| 파일 | 투사체 DefName |
|---|---|
| `Defs/ModernWar/Items/Weapons_ModernWar.xml` | `HD_Bullet_Glock19_Proj` |
| `Defs/ModernWar/Items/RifleAmmunition_Development.xml` | `HD_Bullet_300BLKTACTX_Proj`, `HD_Bullet_76251M80A1_Proj` |
| `Defs/Items/WeaponFamilies_Helod.xml` | `HD_Bullet_M16A4_Proj` |
| `Defs/GreatWar/Items/Weapons_GreatWar.xml` | `HD_Bullet_M1911_Proj`, `HD_Bullet_M1Carbine_Proj`, `HD_Bullet_SprMThree_Proj`, `HD_Bullet_M1Garand_Proj`, `HD_Bullet_M1917HMG_Proj`, `HD_Bullet_M1919A6LMG_Proj`, `HD_Bullet_M2HB_Proj`, `HD_Bullet_M2HB_CAS_Proj`, `HD_Rocket_M6A3HEAT_Proj`, `HD_Bullet_M1897_Proj` |
| `Defs/ColdWar/Items/Weapons_ColdWar.xml` | `HD_BGM71A_Proj`, `HD_Projectile_M167_20mmAPI`, `HD_Projectile_M167_20mmHEI`, `HD_Projectile_M167_20mmHEIT` |
| `Defs/WildWest/Items/Weapons_WildWest.xml` | `HD_Bullet_WinM1887_Proj` |
| `Defs/ColdWar/Items/M79_ColdWar.xml` | `HD_40mmM576Pellet_Proj` |

구형 M16과 비예광 20mm 고폭소이탄은 현재 공용 투사체 구조를 따라 각각 5.56mm·20mm 기준을 공유한다. M60E3의 기존 7.62mm NATO 투사체는 M80A1 선택 탄종과 분리되어 있으므로 이 표의 M80A1 조정 대상에서 제외한다. Combat Extended의 외부 탄약 Def 및 별도 관통 단위는 이 표의 적용 대상이 아니다.
