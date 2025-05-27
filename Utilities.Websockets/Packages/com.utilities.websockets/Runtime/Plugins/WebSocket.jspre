// Licensed under the MIT License. See LICENSE in the project root for license information.

/**
 * Initializes the dynCall_* function table lookups.
 * Thanks to @De-Panther for the following code snippet.
 * Checks if specific dynCall functions exist,
 * if not, it will create them using the getWasmTableEntry function.
 * @see https://discussions.unity.com/t/makedyncall-replacing-dyncall-in-unity-6/1543088
 * @returns {void}
*/
function initializeDynCalls() {
  if (typeof getWasmTableEntry !== "undefined") {
    Module.dynCall_vi = function (cb, arg1) {
      return getWasmTableEntry(cb)(arg1);
    }
    Module.dynCall_vii = function (cb, arg1, arg2) {
      return getWasmTableEntry(cb)(arg1, arg2);
    }
    Module.dynCall_viii = function (cb, arg1, arg2, arg3) {
      return getWasmTableEntry(cb)(arg1, arg2, arg3);
    }
    Module.dynCall_viiii = function (cb, arg1, arg2, arg3, arg4) {
      return getWasmTableEntry(cb)(arg1, arg2, arg3, arg4);
    }
  }
}
/**
 * Checks if the getWasmTableEntry function is defined.
 * @throws {Error} If getWasmTableEntry is not defined.
 */
function checkWasmTableEntryDefined() {
  if (typeof getWasmTableEntry === "undefined") {
    throw new Error("getWasmTableEntry is not defined!");
  }
}
/**
 * Initializes DynCalls back to Unity in the Module.preRun.
 */
Module['preRun'].push(function () {
  initializeDynCalls();
});