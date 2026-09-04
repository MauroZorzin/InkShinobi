# Ink Shinobi

##### _Mauro Zorzin 866001 & Riccardo Chimisso 866009_

## Abstract

Ink Shinobi is a single-player 2.5D stealth-action game developed in Unity 6 using the Universal Render Pipeline. Set in a stylized vision of feudal Japan, the game follows a shinobi with a supernatural connection to ink on a mission to infiltrate a heavily guarded palace and assassinate its Shogun. The objective is simple: enter unseen, strike without hesitation, and disappear into the shadows.

The game combines two-dimensional, ink-illustrated characters with fully three-dimensional environments, physics, lighting, and navigation. Gameplay is presented from a predominantly side-on perspective, preserving the clarity and immediacy of a traditional 2D game while making genuine use of spatial depth. Players travel through mountain passages, moonlit gardens, guarded courtyards, and the interior of the Shogun's palace. The camera and character orientation adapt as the shinobi moves around corners and between different faces of the environment.

Stealth is based on light exposure, concealment, guard vision, and sound. Darkness reduces the player's visibility, while illuminated areas allow guards to detect the shinobi from greater distances. Guards use a short-range vision cone that remains effective even in shadow and a longer cone whose detection strength depends on the player's exposure to light. Detection develops progressively rather than occurring immediately, giving the player a short opportunity to retreat before a suspicious guard confirms the threat.

Guards are controlled through NavMeshAgent navigation and a scripted finite-state machine. They patrol authored routes, begin noticing suspicious activity, chase confirmed targets, investigate sounds, search the player's last known position, and eventually return to their duties. If a guard reaches the shinobi, the player is defeated and may retry the current level. The player can also hide inside designated parts of the environment or throw stones along calculated ballistic trajectories to create sound and lure guards away from important routes.

The game's central ability is the wall switch. By entering an aiming mode, the player slows down time, selects a compatible opposite wall, and transforms into a travelling brushstroke of ink. This ability can be used to cross exposed spaces, bypass obstacles, change the current traversal route, or eliminate eligible guards caught along the trajectory. Before the action is confirmed, the system evaluates the destination, wall alignment, distance, intervening geometry, guard vision, and possible takedown targets. The same evaluation is shared by the visual preview and the final execution, ensuring that the displayed result matches the gameplay outcome.

Standard movement is implemented with a CharacterController constrained to scene-authored line networks. These paths define where the player may travel and support acceleration, deceleration, connected sections, right-angle corners, closed loops, and changes in camera-relative orientation. Physical interactions use three-dimensional colliders, raycasts, overlap tests, and dedicated physics layers. Reusable interaction interfaces support hiding places, collectible objects, mission elements, scene transitions, and animated sliding doors. Some guards carry color-coded keys that are dropped after a takedown and can be used to unlock the corresponding palace doors.

To support informed stealth decisions, the interface translates internal game states into immediate audiovisual feedback. Indicators communicate light exposure, detection progress, guard alert states, available interactions, and valid or obstructed ability targets. Trajectory previews display both the destination of a wall switch and the predicted arc and sound radius of a thrown distraction. Contextual dialogue and staged tutorials introduce each mechanic through play. All project-specific gameplay logic is implemented in modular C# components, separating responsibilities such as movement, perception, navigation, interaction, presentation, and scene management to make the systems easier to configure, maintain, and extend.

The project's visual direction is inspired by Japanese sumi-e ink-wash painting and woodblock prints. Predominantly monochrome environments are combined with high-contrast silhouettes, illustrated sprite characters, selective color accents, and brushstroke-inspired interface elements. Pre-made and original assets are unified through custom URP shaders and renderer features, including selective-color processing, screen-space outlines, occluded-character rendering, stencil-masked doors, ink transitions, dissolves, and particle effects. Lighting and camera movement are curated both for atmosphere and as functional parts of stealth and traversal.

Player and guard animation controllers are connected directly to gameplay logic, allowing movement speed, facing direction, attacks, takedowns, and state changes to determine the displayed animation. The audio system includes separated music and sound-effect mixing, surface-dependent footsteps, guard alerts, environmental ambience, interaction sounds, and spatial reverb.

The complete experience is organized into an opening sequence, palace entrance, garden, palace interior, and cinematic finale. It also includes contextual tutorials, a main menu, pause and confirmation interfaces, configurable audio and display options, death and retry handling, scene transitions, and persistent level progression. Together, these systems create a compact cinematic infiltration game in which observation, route planning, manipulation of enemies, and supernatural ink traversal are equally important to reaching the Shogun unseen.

## Assets

Third-party packages are listed once rather than repeating each included texture, material, model, or audio clip. Standalone assets are grouped by their first use in the game.

- Gingsul Demo font by tkzgraphic: https://www.1001fonts.com/gingsul-demo-font.html
- Super Bugly font: https://fontesk.com/super-bugly-font/
- Main menu background illustration: https://www.rawpixel.com/image/3064491/free-illustration-image-japanese-japan-art
- Button hover sound: https://elements.envato.com/cloth-brush-6316-3ZTGP8X
- Popup appearance paper sound: https://pixabay.com/sound-effects/film-special-effects-large-pamphlet-paper-foley-5-195983/
- Rain Particles by Game Seed Assets: https://assetstore.unity.com/packages/vfx/particles/rain-particles-351846
- Main menu and interface artwork (eyes, parchment, inventory slots, and brush-stroke highlight): project's team work
- Kipish Regular font: https://www.actionfonts.com/font/kipish/
- Background music: https://pixabay.com/music/world-china-chinese-asian-music-346568/
- Ink transition sound: https://pixabay.com/sound-effects/household-sloshing-77211/
- Simple Stylized Slash Pack 2 by Namu: https://assetstore.unity.com/packages/vfx/particles/simple-stylized-slash-pack-2-248665
- Footsteps - Essentials by Nox: https://assetstore.unity.com/packages/audio/sound-fx/foley/footsteps-essentials-189879
- Nature - Essentials by Nox: https://assetstore.unity.com/packages/audio/ambient/nature/nature-essentials-208227
- 96 General Library Bundle: https://assetstore.unity.com/packages/audio/sound-fx/96-general-library-bundle-298038
- Keep Characters Always Visible, Camera Occlusion Cut Out (DOCS), Sample: https://assetstore.unity.com/packages/tools/game-toolkits/keep-characters-always-visible-camera-occlusion-cut-out-for-docs-357594
- 2D Sprite Outline: https://assetstore.unity.com/packages/vfx/shaders/2d-sprite-outline-109669
- Real Stars Skybox Lite: https://assetstore.unity.com/packages/3d/environments/sci-fi/real-stars-skybox-lite-116333
- Idyllic Fantasy Nature by Edenity: https://assetstore.unity.com/packages/3d/environments/fantasy/idyllic-fantasy-nature-260042
- Japanese Machiya Set Kit by xervolt: https://sketchfab.com/3d-models/japanese-machiya-set-kit-d0bb3d915bf9448a9ef46bcb6a6fa6db
- Water splash sound: https://pixabay.com/sound-effects/film-special-effects-water-splash-46402/
- Rock landing sound: https://pixabay.com/sound-effects/film-special-effects-land2-43790/
- Wall-switch sound: https://pixabay.com/sound-effects/film-special-effects-swish-sound-94707/
- Player caught/death sound: https://pixabay.com/sound-effects/horror-horror-orchestra-warning-338415/
- Player and guard sprite sheets and animation artwork: project's team work
- Arrow by Boy Best: https://sketchfab.com/3d-models/arrow-c46f8feb96044a95967feee111488e03
- Animals FREE - Animated Low Poly 3D Models: https://assetstore.unity.com/packages/3d/characters/animals/animals-free-animated-low-poly-3d-models-260727
- Mission scroll pickup sound: https://pixabay.com/sound-effects/film-special-effects-paper-flipping-351981/
- Mission scroll unfolding sound: https://pixabay.com/sound-effects/household-paper-01-87018/
- Mission scroll artwork: project's team work
- Japanese Wood Bridge by btitkin95: https://sketchfab.com/3d-models/japanese-wood-bridge-9d483d5e092544b38ee98abccce55249
- Low Poly Japan 2 by Marcel van Duijn: https://sketchfab.com/3d-models/low-poly-japan-2-a81d57ead93c4b3ca194386f442587d0
- Rock throw sound: https://pixabay.com/sound-effects/film-special-effects-swish-swoosh-woosh-sfx-27-357164/
- Takedown sound by Olivia_Parker: https://pixabay.com/sound-effects/film-special-effects-knife-demo-309903/
- Voices - Essentials: https://assetstore.unity.com/packages/audio/sound-fx/voices/voices-essentials-214441
- Exterior hiding/bush sound: https://pixabay.com/sound-effects/film-special-effects-bushhitwav-14661/
- Key sound: https://pixabay.com/sound-effects/film-special-effects-objkey-keys-throw-jaku5-36260/
- Door closing sound: https://pixabay.com/sound-effects/household-door-close-effect-382710/
- Door opening sound: https://pixabay.com/sound-effects/household-door-opening-397990/
- Color-coded key icon
- Interior hiding sound
- Amanojaku font: https://www.fontspace.com/amanojaku-font-f137423
- Shogun death sound: https://pixabay.com/sound-effects/horror-male-death-sound-128357/
- Free Blood VFX URP: https://assetstore.unity.com/packages/vfx/free-blood-vfx-urp-375130
- Finale sword sound: https://pixabay.com/it/sound-effects/film-ed-effetti-speciali-whoosh-402320/
- Finale candle sound: https://pixabay.com/sound-effects/film-special-effects-crackling-candle-246756/
- Finale video: project's team work
