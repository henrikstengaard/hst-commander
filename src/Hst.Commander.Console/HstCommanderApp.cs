namespace Hst.Commander.Console;

using Terminal.Gui;

public class HstCommanderApp
{
    public virtual void Init(Toplevel top)
    {
        Application.Init();

        var Top = top;
        if (Top == null)
        {
            Top = Application.Top;
        }

        var Win = new Window()
        {
            
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Title = $"Hst Commander"
            //ColorScheme = colorScheme,
        };
        Top.Add(Win);
    }

}