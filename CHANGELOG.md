# Changelog

## [1.1.0] - 2026-02-20

- Add difficulty presets: Stable (default), Strained, and Fragile
  - Stable: Cautious+ re-election threshold, +8 legitimacy
  - Strained: Pleased+ threshold, +6 legitimacy
  - Fragile: Friendly+ threshold, +4 legitimacy
- Select preset via game option toggles in Rules section of game setup
- Remove turn-tier legitimacy scaling (was +8 to +45 across 5 tiers, now flat per preset)
- Simplify from 15 events to 9 (3 candidate counts x 3 presets)
- Elect and re-elect now grant the same legitimacy amount

## [1.0.0] - 2026-02-07

- Initial release: Aristocratic Republic government with elections every 10 turns
- 5 turn tiers with scaling legitimacy bonuses
- Family opinion system (+20 winner, -20 others for 20 turns)
- Re-election requires all families at Cautious+ opinion
- 15 election event variants (3 candidate counts x 5 turn tiers)
