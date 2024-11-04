var UnityWebSocketLibrary = {
  /**
   * Pointer index for WebSocket objects.
   */
  $ptrIndex: 0,
  /**
   * Array of instanced WebSocket objects.
   */
  $webSockets: [],
  /**
   * Initializes the dynCall_* function table lookups.
   * Thanks to @De-Panther for the following code snippet.
   * Checks if specific dynCall functions exist,
   * if not, it will create them using the getWasmTableEntry function.
   * @see https://discussions.unity.com/t/makedyncall-replacing-dyncall-in-unity-6/1543088
   * @returns {void}
  */
  initializeDynCalls: function () {
    Module.dynCall_vi = Module.dynCall_vi || function (cb, arg1) {
      return getWasmTableEntry(cb)(arg1);
    };
    Module.dynCall_vii = Module.dynCall_vii || function (cb, arg1, arg2) {
      return getWasmTableEntry(cb)(arg1, arg2);
    }
    Module.dynCall_viii = Module.dynCall_viii || function (cb, arg1, arg2, arg3) {
      return getWasmTableEntry(cb)(arg1, arg2, arg3);
    }
    Module.dynCall_viiii = Module.dynCall_viiii || function (cb, arg1, arg2, arg3, arg4) {
      return getWasmTableEntry(cb)(arg1, arg2, arg3, arg4);
    }
  },
  /**
   * Create a new WebSocket instance and adds it to the $webSockets array.
   * @param {string} url - The URL to which to connect.
   * @param {string[]} subProtocols - An json array of strings that indicate the sub-protocols the client is willing to speak.
   * @returns {number} - A pointer to the WebSocket instance.
   * @param {function} onOpenCallback - The callback function. WebSocket_OnOpenDelegate(IntPtr websocketPtr) in C#.
   * @param {function} onMessageCallback - The callback function. WebSocket_OnMessageDelegate(IntPtr websocketPtr, IntPtr data, int length, int type) in C#.
   * @param {function} onErrorCallback - The callback function. WebSocket_OnErrorDelegate(IntPtr websocketPtr, IntPtr messagePtr) in C#.
   * @param {function} onCloseCallback - The callback function. WebSocket_OnCloseDelegate(IntPtr websocketPtr, int code, IntPtr reasonPtr) in C#.
   */
  WebSocket_Create: function (url, subProtocols, onOpenCallback, onMessageCallback, onErrorCallback, onCloseCallback) {
    this.initializeDynCalls();
    var urlStr = UTF8ToString(url);

    try {
      var subProtocolsStr = UTF8ToString(subProtocols);
      var subProtocolsArr = subProtocolsStr ? JSON.parse(subProtocolsStr) : undefined;

      for (var i = 0; i < webSockets.length; i++) {
        var instance = webSockets[i];

        if (instance !== undefined && instance.url !== undefined && instance.url === urlStr) {
          console.error('WebSocket connection already exists for URL: ', urlStr);
          return 0;
        }
      }

      var socketPtr = ++ptrIndex;
      webSockets[socketPtr] = {
        socket: null,
        url: urlStr,
        onOpenCallback: onOpenCallback,
        onMessageCallback: onMessageCallback,
        onErrorCallback: onErrorCallback,
        onCloseCallback: onCloseCallback
      };

      if (subProtocolsArr && Array.isArray(subProtocolsArr)) {
        webSockets[socketPtr].subProtocols = subProtocolsArr;
      } else {
        console.error('subProtocols is not an array');
      }

      // console.log(`Created WebSocket object with websocketPtr: ${socketPtr} for URL: ${urlStr}, sub-protocols: ${subProtocolsArr}`);
      return socketPtr;
    } catch (error) {
      console.error('Error creating WebSocket object for URL: ', urlStr, ' Error: ', error);
      return 0;
    }
  },
  /**
   * Get the current state of the WebSocket connection.
   * @param socketPtr - A pointer to the WebSocket object. IntPtr in C#.
   * @returns {number} - The current state of the WebSocket connection.
   */
  WebSocket_GetState: function (socketPtr) {
    try {
      var instance = webSockets[socketPtr];

      if (!instance || !instance.socket) {
        return 0;
      }

      return instance.socket.readyState;
    } catch (error) {
      console.error('Error getting WebSocket state for websocketPtr: ', socketPtr, ' Error: ', error);
      return 3;
    }
  },
  /**
   * Connect the WebSocket connection.
   * @param socketPtr - A pointer to the WebSocket object. IntPtr in C#.
   */
  WebSocket_Connect: function (socketPtr) {
    try {
      var instance = webSockets[socketPtr];

      if (!instance) {
        console.error('WebSocket instance not found for websocketPtr: ', socketPtr);
        return;
      }

      if (!instance.subProtocols || instance.subProtocols.length === 0) {
        instance.socket = new WebSocket(instance.url);
      } else {
        instance.socket = new WebSocket(instance.url, instance.subProtocols);
      }

      instance.socket.binaryType = 'arraybuffer';
      instance.socket.onopen = function () {
        try {
          // console.log('WebSocket connection opened for websocketPtr: ', socketPtr);
          Module.dynCall_vi(instance.onOpenCallback, socketPtr);
        } catch (error) {
          console.error('Error calling onOpen callback for websocketPtr: ', socketPtr, ' Error: ', error);
        }
      };
      instance.socket.onmessage = function (event) {
        try {
          // console.log('Received message for websocketPtr: ', socketPtr, ' with data: ', event.data);
          if (event.data instanceof ArrayBuffer) {
            var array = new Uint8Array(event.data);
            var buffer = Module._malloc(array.length);
            writeArrayToMemory(array, buffer);

            try {
              Module.dynCall_viiii(instance.onMessageCallback, socketPtr, buffer, array.length, 1);
            } finally {
              Module._free(buffer);
            }
          } else if (typeof event.data === 'string') {
            var length = lengthBytesUTF8(event.data) + 1;
            var buffer = Module._malloc(length);
            stringToUTF8(event.data, buffer, length);

            try {
              Module.dynCall_viiii(instance.onMessageCallback, socketPtr, buffer, length, 0);
            } finally {
              Module._free(buffer);
            }
          } else {
            console.error('Error parsing message for websocketPtr: ', socketPtr, ' with data: ', event.data);
          }
        } catch (error) {
          console.error('Error calling onMessage callback for websocketPtr: ', socketPtr, ' Error: ', error);
        }
      };
      instance.socket.onerror = function (event) {
        try {
          console.error('WebSocket error for websocketPtr: ', socketPtr, ' with message: ', event);
          var json = JSON.stringify(event);
          var length = lengthBytesUTF8(json) + 1;
          var buffer = Module._malloc(length);
          stringToUTF8(json, buffer, length);

          try {
            Module.dynCall_vii(instance.onErrorCallback, socketPtr, buffer);
          } finally {
            Module._free(buffer);
          }
        } catch (error) {
          console.error('Error calling onError callback for websocketPtr: ', socketPtr, ' Error: ', error);
        }
      };
      instance.socket.onclose = function (event) {
        try {
          // console.log('WebSocket connection closed for websocketPtr: ', socketPtr, ' with code: ', event.code, ' and reason: ', event.reason);
          var length = lengthBytesUTF8(event.reason) + 1;
          var buffer = Module._malloc(length);
          stringToUTF8(event.reason, buffer, length);

          try {
            Module.dynCall_viii(instance.onCloseCallback, socketPtr, event.code, buffer);
          } finally {
            Module._free(buffer);
          }
        } catch (error) {
          console.error('Error calling onClose callback for websocketPtr: ', socketPtr, ' Error: ', error);
        }
      };
      // console.log('Connecting WebSocket connection for websocketPtr: ', socketPtr);
    } catch (error) {
      console.error('Error connecting WebSocket connection for websocketPtr: ', socketPtr, ' Error: ', error);
    }
  },
  /**
   * Send data to the WebSocket connection.
   * @param socketPtr - A pointer to the WebSocket object. IntPtr in C#.
   * @param data - A pointer to the data to send.
   * @param length - The length of the data to send.
   */
  WebSocket_SendData: function (socketPtr, data, length) {
    try {
      var instance = webSockets[socketPtr];

      if (!instance || !instance.socket || instance.socket.readyState !== 1) {
        console.error('WebSocket connection does not exist for websocketPtr: ', socketPtr);
        return;
      }

      // console.log('Sending message to WebSocket connection for websocketPtr: ', socketPtr, ' with data: ', data, ' and length: ', length);
      instance.socket.send(buffer.slice(data, data + length));
    } catch (error) {
      console.error('Error sending message to WebSocket connection for websocketPtr: ', socketPtr, ' Error: ', error);
    }
  },
  /**
   * Send a string to the WebSocket connection.
   * @param socketPtr - A pointer to the WebSocket object. IntPtr in C#.
   * @param data - The string to send.
   */
  WebSocket_SendString: function (socketPtr, data) {
    try {
      var instance = webSockets[socketPtr];

      if (!instance || !instance.socket || instance.socket.readyState !== 1) {
        console.error('WebSocket connection does not exist for websocketPtr: ', socketPtr);
        return;
      }

      var dataStr = UTF8ToString(data);
      // console.log('Sending message to WebSocket connection for websocketPtr: ', socketPtr, ' with data: ', dataStr);
      instance.socket.send(dataStr);
    } catch (error) {
      console.error('Error sending message to WebSocket connection for websocketPtr: ', socketPtr, ' Error: ', error);
    }
  },
  /**
   * Close the WebSocket connection.
   * @param socketPtr - A pointer to the WebSocket object. IntPtr in C#.
   * @param code - The status code for the close.
   * @param reason - The reason for the close.
   */
  WebSocket_Close: function (socketPtr, code, reason) {
    try {
      var instance = webSockets[socketPtr];

      if (!instance || !instance.socket || instance.socket.readyState >= 2) {
        console.error('WebSocket connection already closed for websocketPtr: ', socketPtr);
        return;
      }

      var reasonStr = UTF8ToString(reason);
      // console.log('Closing WebSocket connection for websocketPtr: ', socketPtr, ' with code: ', code, ' and reason: ', reasonStr);
      instance.socket.close(code, reasonStr);
    } catch (error) {
      console.error('Error closing WebSocket connection for websocketPtr: ', socketPtr, ' Error: ', error);
    }
  },
  /**
   * Destroy a WebSocket object.
   * @param socketPtr - A pointer to the WebSocket object. IntPtr in C#.
   */
  WebSocket_Dispose: function (socketPtr) {
    try {
      // console.log('Disposing WebSocket object with websocketPtr: ', socketPtr);
      delete webSockets[socketPtr];
    } catch (error) {
      console.error('Error disposing WebSocket object with websocketPtr: ', socketPtr, ' Error: ', error);
    }
  }
};

autoAddDeps(UnityWebSocketLibrary, '$ptrIndex');
autoAddDeps(UnityWebSocketLibrary, '$webSockets');
mergeInto(LibraryManager.library, UnityWebSocketLibrary);
