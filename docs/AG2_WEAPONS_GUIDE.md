# Hướng dẫn chi tiết: Tạo weapons.vdata cho AG2

## Giới thiệu

Sau update AnimGraph2 (AG2), Valve thay đổi cách weapon skins hoạt động. Thay vì dùng `SetModel()`, giờ phải dùng `ChangeSubclass()` với subclass được đăng ký trong `weapons.vdata`.

---

## Phần 1: Chuẩn bị công cụ

### 1.1 Source2Viewer
- Download: https://github.com/ValveResourceFormat/ValveResourceFormat/releases
- Dùng để decompile file vdata từ Valve

### 1.2 Text Editor
- VS Code hoặc Notepad++ (hỗ trợ file lớn)

### 1.3 VPK Tool
- Dùng `vpk.exe` từ CS2 SDK hoặc VRF

---

## Phần 2: Decompile weapons.vdata gốc

### 2.1 Tìm file gốc
```
Path: steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk
File bên trong: scripts/weapons.vdata
```

### 2.2 Extract bằng Source2Viewer
1. Mở Source2Viewer
2. File → Open → chọn `pak01_dir.vpk`
3. Navigate đến `scripts/weapons.vdata`
4. Right-click → Export → Decompile

### 2.3 File output
Bạn sẽ có file `weapons.vdata` với nội dung kiểu:
```vdata
"weapon_ak47" = {
    m_nKillAward = 300
    m_nDamage = 36
    m_iMaxClip1 = 30
    // ... nhiều properties khác
}

"weapon_awp" = {
    m_nKillAward = 100
    m_nDamage = 115
    m_iMaxClip1 = 5
    // ...
}
```

---

## Phần 3: Hiểu cấu trúc vdata

### 3.1 Weapon entry cơ bản
```vdata
"weapon_awp" = {
    // === DAMAGE & STATS ===
    m_nKillAward = 100                    // Tiền thưởng khi kill
    m_nDamage = 115                       // Damage per shot
    m_iMaxClip1 = 5                       // Số đạn trong băng
    m_iMaxClip2 = 0                       // Secondary ammo
    m_iDefaultClip1 = 5                   // Đạn mặc định
    m_iDefaultClip2 = 0
    
    // === ACCURACY ===
    m_flInaccuracyStand = [ 0.0, 0.0 ]
    m_flInaccuracyCrouch = [ 0.0, 0.0 ]
    m_flInaccuracyJump = [ 0.0, 0.0 ]
    m_flInaccuracyMove = [ 0.0, 0.0 ]
    
    // === RECOIL ===
    m_flRecoilMagnitude = [ 0.0, 0.0 ]
    m_flRecoilAngle = [ 0.0, 0.0 ]
    
    // === TIMING ===
    m_flCycleTime = [ 1.5, 1.5 ]          // Fire rate
    m_flDeployDuration = 1.0              // Deploy time
    
    // === MOVEMENT ===
    m_flMaxSpeed = [ 200.0, 200.0 ]
    
    // === ZOOM ===
    m_nZoomLevels = 2
    m_nZoomFOV1 = 40
    m_nZoomFOV2 = 15
    
    // === MODELS - QUAN TRỌNG! ===
    m_szWorldModel = resource_name:"weapons/models/awp/awp.vmdl"
    m_szModel_AG2 = resource_name:"weapons/models/awp/awp.vmdl"
    
    // === ANIMATION - QUAN TRỌNG! ===
    m_szAnimSkeleton = resource_name:"animation/skeletons/weapons/awp.vnmskel"
    
    // === IDENTITY ===
    m_szName = "weapon_awp"
    _class = "weapon_awp"
    _base = "507"                         // Item definition index
    
    // === SOUNDS ===
    m_aShootSounds = {
        WEAPON_SOUND_SINGLE = soundevent:"Weapon_AWP.Single"
        WEAPON_SOUND_RELOAD = soundevent:"Weapon_AWP.Reload"
    }
    
    // === CLASSIFICATION ===
    m_WeaponType = "WEAPONTYPE_SNIPER_RIFLE"
    m_GearSlot = "GEAR_SLOT_RIFLE"
    m_GearSlotPosition = 0
}
```

### 3.2 Các field QUAN TRỌNG cho custom weapons

| Field              | Mô tả                 | Bắt buộc thay đổi? |
| ------------------ | --------------------- | ------------------ |
| `m_szWorldModel`   | Model 3D của weapon   | ✅ CÓ               |
| `m_szModel_AG2`    | Model cho AnimGraph2  | ✅ CÓ               |
| `m_szAnimSkeleton` | Animation skeleton    | ❌ Giữ nguyên       |
| `m_szName`         | Tên weapon            | ❌ Giữ nguyên       |
| `_class`           | Weapon class          | ❌ Giữ nguyên       |
| `_base`            | Item definition index | ❌ Giữ nguyên       |

---

## Phần 4: Tạo custom subclass

### 4.1 Format tên subclass
```
{weapon_base}+{unique_number}

Ví dụ:
- weapon_ak47+1001
- weapon_awp+1002
- weapon_m4a1+1003
- weapon_knife_karambit+1550
```

**Lưu ý:** 
- Số unique PHẢI là duy nhất trong toàn bộ file
- Nên dùng số lớn (1000+) để tránh conflict với Valve

### 4.2 Copy toàn bộ weapon base
Tìm weapon base (vd: `weapon_awp`) và copy TOÀN BỘ content:

```vdata
// COPY TOÀN BỘ TỪ weapon_awp, CHỈ ĐỔI TÊN VÀ MODEL
"weapon_awp+1001" = {
    // Copy nguyên xi tất cả từ weapon_awp
    m_nKillAward = 100
    m_nDamage = 115
    m_iMaxClip1 = 5
    m_iMaxClip2 = 0
    m_iDefaultClip1 = 5
    m_iDefaultClip2 = 0
    m_bAllowFlipping = true
    m_bBuiltRightHanded = true
    m_bIsFullAuto = false
    m_nNumBullets = 1
    m_bMeleeWeapon = false
    m_iWeight = 30
    m_iRumbleEffect = 1
    m_nPrimaryReserveAmmoMax = 30
    m_nSecondaryReserveAmmoMax = 0
    m_flInaccuracyJumpInitial = 0.6
    m_flInaccuracyJumpApex = 0.9
    m_flInaccuracyReload = 0.2
    m_flDeployDuration = 1.0
    m_flDisallowAttackAfterReloadStartDuration = 0.5
    m_nSpreadSeed = 28474
    m_flRecoveryTimeCrouch = 0.45
    m_flRecoveryTimeStand = 0.7
    m_flRecoveryTimeCrouchFinal = 0.45
    m_flRecoveryTimeStandFinal = 0.7
    m_nRecoveryTransitionStartBullet = 0
    m_nRecoveryTransitionEndBullet = 0
    m_flHeadshotMultiplier = 4.0
    m_flArmorRatio = 1.95
    m_flPenetration = 2.5
    m_flFlinchVelocityModifierLarge = 0.4
    m_flFlinchVelocityModifierSmall = 0.5
    m_flRange = 8192.0
    m_flRangeModifier = 0.99
    m_eSilencerType = "WEAPONSILENCER_NONE"
    m_nCrosshairMinDistance = 8
    m_nCrosshairDeltaDistance = 3
    m_flAttackMovespeedFactor = 1.0
    m_bUnzoomsAfterShot = true
    m_bHideViewModelWhenZoomed = true
    m_nZoomLevels = 2
    m_nZoomFOV1 = 40
    m_nZoomFOV2 = 15
    m_flZoomTime0 = 0.1
    m_flZoomTime1 = 0.05
    m_flZoomTime2 = 0.05
    m_flInaccuracyPitchShift = 0.0
    m_flInaccuracyAltSoundThreshold = 0.0
    m_bHasBurstMode = false
    m_bIsRevolver = false
    m_bCannotShootUnderwater = true
    m_flCycleTime = [ 1.463, 1.463, ]
    m_flMaxSpeed = [ 200.0, 200.0, ]
    m_flSpread = [ 0.0, 0.0, ]
    m_flInaccuracyCrouch = [ 2.8, 0.0, ]
    m_flInaccuracyStand = [ 3.5, 0.0, ]
    m_flInaccuracyJump = [ 0.98, 0.98, ]
    m_flInaccuracyLand = [ 0.5, 0.5, ]
    m_flInaccuracyLadder = [ 200.0, 200.0, ]
    m_flInaccuracyFire = [ 0.0, 0.0, ]
    m_flInaccuracyMove = [ 200.0, 200.0, ]
    m_flRecoilAngle = [ 0.0, 0.0, ]
    m_flRecoilAngleVariance = [ 10.0, 10.0, ]
    m_flRecoilMagnitude = [ 70.0, 70.0, ]
    m_flRecoilMagnitudeVariance = [ 0.0, 0.0, ]
    m_nTracerFrequency = 0
    m_bAutoSwitchFrom = true
    m_bAutoSwitchTo = true
    _base = "507"
    
    taxonomy = {
        weapon = true
        rifle = true
        sniper = true
    }
    
    _class = "weapon_awp"
    m_GearSlot = "GEAR_SLOT_RIFLE"
    m_GearSlotPosition = 0
    m_DefaultLoadoutPosition = "LOADOUT_POSITION_RIFLE5"
    
    // ============================================
    // THAY ĐỔI CÁC DÒNG NÀY VỚI MODEL CỦA BẠN
    // ============================================
    m_szWorldModel = resource_name:"phase2/weapons/models/2en0w/ak47_zaomeng/awp_zaomeng_ag2.vmdl"
    m_szModel_AG2 = resource_name:"phase2/weapons/models/2en0w/ak47_zaomeng/awp_zaomeng_ag2.vmdl"
    // ============================================
    
    // GIỮA NGUYÊN DÒNG NÀY - skeleton của weapon base
    m_szAnimSkeleton = resource_name:"animation/skeletons/weapons/awp.vnmskel"
    
    m_WeaponType = "WEAPONTYPE_SNIPER_RIFLE"
    
    m_aShootSounds = {
        WEAPON_SOUND_EMPTY = soundevent:"Default.ClipEmpty_Rifle"
        WEAPON_SOUND_SINGLE = soundevent:"Weapon_AWP.Single"
        WEAPON_SOUND_RELOAD = soundevent:"Weapon_AWP.Reload"
        WEAPON_SOUND_NEARLYEMPTY = soundevent:"Default.nearlyempty"
    }
    
    m_nPrice = 4750
    m_nRecoilSeed = 28474
    
    // GIỮA NGUYÊN
    m_szName = "weapon_awp"
}
```

### 4.3 Tóm tắt những gì cần thay đổi

| Thay đổi           | Giá trị cũ     | Giá trị mới         |
| ------------------ | -------------- | ------------------- |
| **Tên entry**      | `"weapon_awp"` | `"weapon_awp+1001"` |
| **m_szWorldModel** | Model gốc      | Path model của bạn  |
| **m_szModel_AG2**  | Model gốc      | Path model của bạn  |

**KHÔNG thay đổi:**
- `m_szAnimSkeleton` - giữ skeleton của weapon base
- `m_szName` - giữ tên weapon base
- `_class` - giữ class của weapon base
- Tất cả stats khác - copy nguyên xi

---

## Phần 5: Ví dụ hoàn chỉnh

### 5.1 Thêm AWP Zaomeng

Thêm vào cuối file `weapons.vdata`:

```vdata
// ===== CUSTOM WEAPONS - AWP ZAOMENG =====
"weapon_awp+1001" = {
    m_nKillAward = 100
    m_nDamage = 115
    m_iMaxClip1 = 5
    // ... (copy toàn bộ từ weapon_awp gốc)
    
    m_szWorldModel = resource_name:"phase2/weapons/models/2en0w/ak47_zaomeng/awp_zaomeng_ag2.vmdl"
    m_szModel_AG2 = resource_name:"phase2/weapons/models/2en0w/ak47_zaomeng/awp_zaomeng_ag2.vmdl"
    m_szAnimSkeleton = resource_name:"animation/skeletons/weapons/awp.vnmskel"
    m_szName = "weapon_awp"
    _class = "weapon_awp"
    _base = "507"
}

// ===== CUSTOM WEAPONS - AK47 ZAOMENG =====
"weapon_ak47+1002" = {
    m_nKillAward = 300
    m_nDamage = 36
    m_iMaxClip1 = 30
    // ... (copy toàn bộ từ weapon_ak47 gốc)
    
    m_szWorldModel = resource_name:"phase2/weapons/models/2en0w/ak47_zaomeng/ak47_zaomeng_ag2.vmdl"
    m_szModel_AG2 = resource_name:"phase2/weapons/models/2en0w/ak47_zaomeng/ak47_zaomeng_ag2.vmdl"
    m_szAnimSkeleton = resource_name:"animation/skeletons/weapons/ak47.vnmskel"
    m_szName = "weapon_ak47"
    _class = "weapon_ak47"
    _base = "507"
}
```

### 5.2 Knife example (phức tạp hơn)

```vdata
"weapon_knife_karambit+1550" = {
    m_nKillAward = 1500
    m_nDamage = 50
    m_iMaxClip1 = 0
    m_iMaxClip2 = 0
    m_iDefaultClip1 = 1
    m_iDefaultClip2 = 1
    m_bAllowFlipping = true
    m_bBuiltRightHanded = true
    m_bIsFullAuto = false
    m_nNumBullets = 1
    m_bMeleeWeapon = true
    m_iWeight = 0
    m_iRumbleEffect = 9
    m_nPrimaryReserveAmmoMax = 0
    m_nSecondaryReserveAmmoMax = 0
    m_flCycleTime = [ 0.15, 0.3, ]
    m_flMaxSpeed = [ 250.0, 250.0, ]
    
    _base = "507"
    
    taxonomy = {
        weapon = true
        self_damage_on_miss__inflicts_damage = true
        melee = true
    }
    
    _class = "weapon_knife"
    m_GearSlot = "GEAR_SLOT_KNIFE"
    m_GearSlotPosition = 0
    m_DefaultLoadoutPosition = "LOADOUT_POSITION_MELEE"
    
    m_szWorldModel = resource_name:"phase2/weapons/models/aur1c/karambit_mfsn/karambit_mfsn_ag2.vmdl"
    m_szModel_AG2 = resource_name:"phase2/weapons/models/aur1c/karambit_mfsn/karambit_mfsn_ag2.vmdl"
    m_szAnimSkeleton = resource_name:"animation/skeletons/weapons/knife_karambit.vnmskel"
    
    m_WeaponType = "WEAPONTYPE_KNIFE"
    
    m_aShootSounds = {
        WEAPON_SOUND_EMPTY = soundevent:"Default.ClipEmpty_Rifle"
        WEAPON_SOUND_SINGLE = soundevent:"Weapon_Knife.Slash"
        WEAPON_SOUND_RELOAD = soundevent:"Default.Reload"
    }
    
    m_nPrice = 0
    m_szName = "weapon_knife_karambit"
}
```

---

## Phần 6: Pack và Deploy

### 6.1 Cấu trúc folder addon
```
my_weapons_addon/
├── scripts/
│   └── weapons.vdata          ← File vdata đã sửa
├── phase2/
│   └── weapons/
│       └── models/
│           └── 2en0w/
│               └── ak47_zaomeng/
│                   ├── awp_zaomeng_ag2.vmdl
│                   ├── awp_zaomeng_ag2.vmdl_c   ← File compiled
│                   ├── ak47_zaomeng_ag2.vmdl
│                   └── ak47_zaomeng_ag2.vmdl_c
└── addoninfo.txt
```

### 6.2 Pack thành VPK
```bash
vpk.exe my_weapons_addon
```

Output: `my_weapons_addon.vpk`

### 6.3 Deploy lên server
Copy VPK vào:
```
game/csgo/addons/my_weapons_addon.vpk
```

---

## Phần 7: Config plugin

### 7.1 Cập nhật zWeapons.json

Thêm field `subclass` vào config:

```json
{
    "Weapons": {
        "Zaomeng": {
            "image": "zaomeng.png",
            "name": "Zaomeng Skins",
            "weapon_awp": {
                "AWP Zaomeng": {
                    "name": "AWP Zaomeng",
                    "uniqueid": "awp_zaomeng_ag2",
                    "subclass": "weapon_awp+1001",
                    "model": "phase2/weapons/models/2en0w/ak47_zaomeng/awp_zaomeng_ag2.vmdl",
                    "image_gun": "awp_zaomeng_ag2.png"
                }
            },
            "weapon_ak47": {
                "AK-47 Zaomeng": {
                    "name": "AK-47 Zaomeng",
                    "uniqueid": "ak47_zaomeng_ag2",
                    "subclass": "weapon_ak47+1002",
                    "model": "phase2/weapons/models/2en0w/ak47_zaomeng/ak47_zaomeng_ag2.vmdl",
                    "image_gun": "ak47_zaomeng_ag2.png"
                }
            }
        }
    }
}
```

### 7.2 Plugin sẽ gọi
```csharp
weapon.AcceptInput("ChangeSubclass", weapon, weapon, "weapon_awp+1001");
```

---

## Checklist

- [ ] Decompile weapons.vdata từ pak01_dir.vpk
- [ ] Copy toàn bộ weapon base entry
- [ ] Đổi tên entry thành `weapon_xxx+number`
- [ ] Thay đổi `m_szWorldModel` và `m_szModel_AG2`
- [ ] Giữ nguyên `m_szAnimSkeleton`, `m_szName`, `_class`
- [ ] Model file có cả `.vmdl` và `.vmdl_c`
- [ ] Pack thành VPK
- [ ] Deploy VPK vào server
- [ ] Restart server
- [ ] Test với plugin
