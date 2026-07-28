// "PresentationManager.Application" (our Clean Architecture project) and "System.Windows.Forms.Application"
// share the "PresentationManager" namespace root, so the WinForms static class gets shadowed everywhere in
// this project. This alias disambiguates it once instead of fully-qualifying at every call site.
global using WinFormsApp = System.Windows.Forms.Application;
