// The page's two connections to .NET, kept in one place so no other module needs to know how they are
// made (SEC-7).
//
//   - `ref` is the DotNetObjectReference the viewer's interop object handed over in `init`. Callbacks go
//     to that one instance — never to a static method that any script in the page could also call — and
//     the reference is disposed when the viewer goes.
//   - `config` is what .NET said at start: the limits, the file types and the shortcuts. They are written
//     down once, in C#, and never repeated in JavaScript.
export const bridge = { ref: null, config: null };

/// Calls an instance method of the viewer's interop object. Before `init` there is nobody to call, and
/// a call that fails (the viewer is gone) is reported, never thrown into the event handler it came from.
export function call(method, ...args) {
    const ref = bridge.ref;
    if (!ref) return;
    ref.invokeMethodAsync(method, ...args).catch(e => console.warn(method, e));
}

/// The configuration .NET passed to `init`. A module that needs it before then is a bug, and says so.
export function config() {
    if (!bridge.config) throw new Error('main.js init() has not been called yet');
    return bridge.config;
}
