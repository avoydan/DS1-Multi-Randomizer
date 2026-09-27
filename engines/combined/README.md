# Combined items and enemies engine

Primary upstream candidate: https://github.com/kellernz1/ds-randomizer

The production integration will use a pinned GPL-3.0-only fork and a stable,
headless process boundary. Do not place game files in this directory.

The backend remains responsible for:

- workspace creation;
- configuration validation;
- final package manifests;
- activation and restore;
- orchestration with FogMod.

The engine is invoked once for both item and enemy capabilities. Its own
installer and restore flow must not become a second backup authority.
