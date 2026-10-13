// Blazor JS initializer of the client: the host page's boot indicator (App.razor, in the page's language) gives way to the application
// once the WebAssembly runtime has started; from then on the layouts show their own loading state in the same language.
export function afterWebAssemblyStarted() {
  document.getElementById("app-boot")?.remove();
}
