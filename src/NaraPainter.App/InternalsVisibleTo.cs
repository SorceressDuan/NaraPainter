using System.Runtime.CompilerServices;

// The transformation tests read a layer's working buffer directly. That member stays internal because
// nothing outside the app should hand a raw buffer around; the tests need it to prove that an undo put
// the original pixels back rather than only the original size.
[assembly: InternalsVisibleTo("NaraPainter.Tests")]
