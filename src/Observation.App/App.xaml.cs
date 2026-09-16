using System.Windows;

namespace Observation.App;

public partial class App : Application
{
    // Именованный Mutex для единственного экземпляра (см. «Хранение данных, окно,
    // локализация» в плане) и запуск tray/NotifyIcon подключатся здесь на этапе
    // реализации — сейчас это только точка входа, чтобы решение собиралось.
}
