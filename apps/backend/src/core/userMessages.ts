/**
 * Plain-language text for failures the person using the app can see. The exact reason (code, paths, hashes) always stays in
 * the run log and the manifest; this only decides what the primary message says. A message that is already written for
 * people (the fog randomizer preparation, custom enemy pool refusals) is passed through unchanged.
 */

const exact: Record<string, string> = {
  WORKSPACE_UNSAFE: 'The app could not find a safe place to work. Restart the app and try again.',
  GAME_INVALID: 'This folder does not look like a complete Dark Souls Remastered installation. Verify the game files in Steam, or choose the game folder again.',
  PRISTINE_IMPORT_REQUIRED: 'Dark Souls Remastered has not been checked yet. Wait a moment and try again.',
  PRISTINE_SOURCE_UNSAFE: 'Your game files could not be verified. Verify the game files in Steam, then try again.',
  PRISTINE_SOURCE_INVALID: 'Your game files could not be verified. Verify the game files in Steam, then try again.',
  COMBINED_OUTPUT_MISMATCH: 'The randomized files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  COMBINED_OUTPUT_INVALID: 'The randomized files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  FOG_OUTPUT_MISMATCH: 'The randomized world files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  FOG_OUTPUT_INVALID: 'The randomized world files failed a safety check, so nothing was changed. Try again, or use a different seed.',
  FOG_BRIDGE_INPUT_INVALID: 'The fog randomizer could not be set up with these settings. Try again, or change the fog settings.',
  FOG_STAGING_FAILED: 'The fog randomizer could not be set up. Check that the disk has free space and try again.',
  FOG_RETRIES_EXHAUSTED: 'The fog randomizer could not find a working layout for this seed and these settings. Try another seed, or switch off some fog options.',
  FOG_SEED_UNSOLVABLE: 'The fog randomizer could not find a working layout for this seed and these settings. Try another seed, or switch off some fog options.',
  FOG_UPSTREAM_INCOMPATIBLE: 'These item and fog settings cannot be combined yet. Your game was not changed. Switch off the fog randomizer or some extra item categories (NPC gifts, enemy drops, shops) and try again.',
  FOG_ITEM_RULES_UNREADABLE: 'The fog randomizer data could not be read. Click Generate world again; if it keeps failing, restart the app.',
  KEY_PLACEMENT_MODEL_UNAVAILABLE: 'Key placement needs the world data of the fog randomizer, which could not be prepared. Click Generate world again, or turn Key placement off.',
  RACE_LOCATION_INVALID: 'The Race Mode placement did not pass its final check, so nothing was written. Try another seed, or choose another key placement; the details are under Advanced.',
  RACE_PLUS_LOCATION_INVALID: 'The Race Mode + placement did not pass its final check, so nothing was written. Try another seed, or choose another key placement; the details are under Advanced.',
  DIFFICULTY_POLICY_UNSATISFIED: 'The items could not be arranged for the selected difficulty. Try another seed, or choose Fair.',
  KEY_PLACEMENT_UNSATISFIABLE: 'No safe place was found for every key with these settings. Try another seed, or turn Key placement off.',
  KEY_PLACEMENT_FILL_IMPOSSIBLE: 'The items could not be arranged around the keys with these settings. Try another seed, or turn Key placement off.',
  KEY_PLACEMENT_INVALID: 'The key placement did not pass its final safety check, so nothing was written. Try another seed, or turn Key placement off; the details are under Advanced.',
  KEY_PLACEMENT_MODEL_INVALID: 'The world data used for key placement is not usable. Restart the app and try again; the details are under Advanced.',
  KEY_PLACEMENT_MODEL_MISSING: 'Key placement needs the world data of the fog randomizer, which was not available. Try again, or turn Key placement off.',
  KEY_PLACEMENT_NOTHING_TO_PLACE: 'Key placement found no key it can move with these settings. Turn Key placement off.',
  FOG_KEY_CONFLICT: 'The fog randomizer could not find a working layout around the shuffled keys for this seed. Try another seed, or turn Key placement or some fog options off.',
  COMBINED_RETRIES_EXHAUSTED: 'No working combination was found for this seed. Try another seed.',
  GENERATION_PROFILE_MISMATCH: 'This copy of the app cannot reproduce that world exactly, so nothing was generated. Update or reinstall DS1 Multi Randomizer.',
  MANIFEST_WRITE_FAILED: 'Your randomized files could not be saved. Check that the disk has free space and try again.',
  DATA_TOOL_MISSING: 'The game data tool of the app could not be started. Reinstall DS1 Multi Randomizer; the details are under Advanced.',
  DATA_TOOL_FAILED: 'The game data could not be processed. Try again; if it keeps happening, verify the game files in Steam. The details are under Advanced.',
  CATALOG_UNAVAILABLE: 'The game data could not be prepared. Click Generate world again; if it keeps failing, restart the app.',
  CATALOG_UNREADABLE: 'The game data could not be read. Click Generate world again; if it keeps failing, restart the app.',
  CATALOG_STALE: 'The prepared game data is out of date. Restart the app and try again.',
  CATALOG_SOURCE_MISMATCH: 'The prepared game data does not belong to this installation. Restart the app and try again; if the game was updated, verify its files in Steam.',
  OUTPUT_WRITE_FAILED: 'Your randomized files could not be saved. Check that the disk has free space and try again.',
  NO_RANDOMIZATION_FEATURES_ENABLED: 'Nothing is switched on to randomize. Turn on items, enemies or bosses, or the fog randomizer.',
  WORKSPACE_INSIDE_GAME_DIRECTORY: 'The app could not find a safe place to work. Restart the app and try again.',
  INVALID_CONFIG: 'These settings cannot be used. Change them and try again.',
  FOG_HOST_MISSING: 'The fog randomizer component is missing. Reinstall DS1 Multi Randomizer, or switch off the fog randomizer.',
  FOG_HOST_CRASHED: 'The fog randomizer stopped unexpectedly. Your game was not changed. Try again; if it keeps happening, switch off the fog randomizer.',
  FOG_HOST_TIMEOUT: 'The fog randomizer took too long and was stopped. Your game was not changed. Try again, or try another seed.',
  FOG_MERGE_CONFLICT: 'The fog randomizer and the other randomization could not be combined for this seed. Your game was not changed. Try another seed.',
  FOG_DATA_MISSING: 'The fog randomizer data is not ready yet. Wait until it has been prepared, or click Retry.',
};

/** Codes whose message was already written for people (they may still carry a technical `CODE:` prefix to strip). */
const readable = /^(WORLD_DATA_|FOG_PREREQUISITES_MISSING|ENEMY_PLACEMENT_CUSTOM_POOL_INVALID|ENEMY_PLACEMENT_INFEASIBLE)/u;

export const genericFailureMessage = 'Something went wrong while creating your randomized game. Try another seed; the details are under Advanced.';

export const genericWorldFailureMessage = 'The fog randomizer stopped with an unexpected problem. Your game was not changed. Try again; if it keeps happening, switch off the fog randomizer. The details are under Advanced.';

export function userMessageFor(code: string, technical: string): string {
  if (exact[code]) return exact[code];
  if (readable.test(code)) return technical.replace(/^[A-Z][A-Z0-9_]*:\s*/u, '');
  if (code.startsWith('FOG_')) return genericWorldFailureMessage;
  return genericFailureMessage;
}
