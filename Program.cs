using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Memory;
using Recompiled;

var title = "Jet Moto 2 Recompiled";

Dispatcher.Tolerant = true;
JetMoto2.Diag.Start();
JetMoto2.TestInput.Start();
JetMoto2.Cheats.Register();

RecompOne.Runtime.Runtime.Run(() => Entry.Run(new PSMemory(), args.Length > 0 ? args[0] : null, title));
return 0;
