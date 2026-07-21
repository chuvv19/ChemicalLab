***********************************************
*              LIQUID VOLUME PRO              *
*           By Ramiro Oliva (Kronnect)        *   
*                 README FILE                 *
***********************************************


How to use this asset
---------------------

Thanks for purchasing LIQUID VOLUME PRO!

Important: please read the brief quick start guide located in LiquidVolumePro/Documentation.


Support
-------

Have any question or issue?
* Support-Web: https://kronnect.com/docs/liquid-volume/
* Support-Discord: https://discord.gg/EH2GMaM
* Email: contact@kronnect.com
* Twitter: @Kronnect


Other Cool Assets!
------------------

Check our other assets on the Asset Store publisher page:
https://assetstore.unity.com/publishers/15018



Version history
---------------

Version 11.1.2
- [Fix] Fixed missing shader variant issue

Version 11.1.1
- [Fix] Fixed an issue when compacting/mixing layers when one of the layers amount is zero

Version 11.1
- Added "Rim Power" option
- Added rim effect to multiple layer style

Version 11.0
- Added stencil buffer options
- Added support for spherical harmonics lighting when interleaved rendering is enabled

Version 10.1
- Added "Rim Color" option

Version 10.0.3
- [Fix] Fixes for WebGL depth aware rendering

Version 10.0.1
- [Fix] Fixes for interleaved rendering

Version 10.0
- Added support for Render Graph (Unity 2023.3)

Version 5.5
- Improved flask shader compatibility in Unity 2022 URP
- [Fix] Fixed depth aware issue with orthographic camera option enabled

Version 5.4
- Current "Global Alpha" renamed to "Global Density". Added "Final Alpha Multiplier" option.

Version 5.3.1
- [Fix] Fixes to interleaved rendering

Version 5.3
- Added "Foam Texture" option to inspector

Version 5.2
- Added new options to use the directional light direction and color

Version 5.1
- Added "Limit Vertical Range" option

Version 5.0.1
- [Fix] Fixed multi_view rendering on Quest 2 with irregular topology

Version 5.0
- Added support for Unity 2022

Version 4.5.1
- [Fix] Fixed issue rendering irregular topology in Single Pass Instanced VR mode when interleaved rendering option is disabled

Version 4.5
- Minimum Unity version required is now 2020.3.16
- Some shader optimizations related to point light support
- [Fix] Fixed issue when ignoring gravity and using multiple layers

Version 4.4
- API: added GetMeshVolumeWS, GetMeshVolumeWSUnderLevel, GetMeshVolumeWSFast, GetMeshVolumeWSUnderLevelFast methods. They return approximations of the mesh volume
- API: added optional "rotationCompensation" parameter to GetSpillPoint method
- [Fix] Fixed inspector overlap issue in Unity 2021.3.3 due to a reorderable list bug

Version 4.3.2
- API: added onPropertiesChanged event
- Added "Interleaved Rendering" to depth prepass render feature. This option improves results when overlapping irregular liquid volumes.

Version 4.2
- Added new algorithms for "Rotation Level Bias" option. Now this option has 3 values: None, Fast and Accurate.
- [Fix] Fixed color difference between multiple and non-multiple styles when in Linear Color Space

Version 4.1
- Added new detail level: "Simple No Flask"
- Added "Use Light Color" option (refers to directional light)
- Added option to disable noise to improve performance on lower end devices
- [Fix] Fixed empty slot issue in materials list when switching prefab mode

Version 4.0.5
- Memory and loading optimizations when using many instances of same flask
- [Fix] Fixed an issue with Unity 2021.2 that affects shader variants in a build

Version 4.0.4
- Memory optimizations for bubbles option

Version 4.0.3
- [Fix] Fixed specular visible in objects without flasks

Version 4.0.2
- [Fix] Fixed flask mesh being partially visible when refraction blur is enabled on non-flask types

Version 4.0.1
- [Fix] Fixed miscible amount calculation bug

Version 4.0
- First version compatible with URP


License information
-------------------
- Liquid Volume developed by Ramiro Oliva @Kronnect.
- If purchased on the Asset Store, this asset is subjet to Asset Store EULA (https://unity3d.com/es/legal/as_terms). 
- If purchased on other site, Liqud Volume is licensed under specific terms by you and (C) 2018-2021 Ramiro Oliva
- Uses LiquidVolumeFX.MIConvexHull library (MIT license) for the Fix Mesh feature