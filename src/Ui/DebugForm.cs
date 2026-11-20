using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace DeckMapper;

/// <summary>Podgląd na żywo: co widzi aplikacja z kontrolera. Do sprawdzania mapowania bitów.</summary>
sealed class DebugForm : Form
{
    readonly ControllerWorker _worker;
    readonly Mapper _mapper;
    readonly Label _label;
    readonly Timer _timer;
    readonly DeckState _state = new();

    public DebugForm(ControllerWorker worker, Mapper mapper)
    {
        _worker = worker;
        _mapper = mapper;

        Text = "DeckMapper – podgląd kontrolera";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(820, 520);
        BackColor = Color.FromArgb(24, 24, 28);
        ForeColor = Color.Gainsboro;

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11f),
            Padding = new Padding(12),
        };
        Controls.Add(_label);

        _timer = new Timer { Interval = 50 };
        _timer.Tick += (_, _) => RefreshView();
        _timer.Start();
    }

    void RefreshView()
    {
        var sb = new StringBuilder();
        sb.AppendLine(_worker.Connected ? "Kontroler: połączony" : "Kontroler: brak – szukam…");
        sb.AppendLine(_mapper.Paused ? "Mapowanie: WSTRZYMANE" : "Mapowanie: aktywne");
        sb.AppendLine();

        if (_worker.TryGetSnapshot(_state))
        {
            var s = _state;
            sb.AppendLine($"Lewa gałka     X {s.LeftX,6:0.00}   Y {s.LeftY,6:0.00}");
            sb.AppendLine($"Prawa gałka    X {s.RightX,6:0.00}   Y {s.RightY,6:0.00}");
            sb.AppendLine($"Triggery       L {s.LeftTrigger,6:0.00}   R {s.RightTrigger,6:0.00}");
            sb.AppendLine($"Lewy pad       X {s.LeftPadX,6:0.00}   Y {s.LeftPadY,6:0.00}   {(s.LeftPadTouched ? "dotyk" : "")}");
            sb.AppendLine($"Prawy pad      X {s.RightPadX,6:0.00}   Y {s.RightPadY,6:0.00}   {(s.RightPadTouched ? "dotyk" : "")}");
            sb.AppendLine();

            var pressed = Enum.GetValues<DeckControl>().Where(c => s.Buttons[(int)c]);
            sb.AppendLine("Wciśnięte: " + string.Join(", ", pressed));
            sb.AppendLine();

            sb.AppendLine("Surowy raport (bajty 8–15 = przyciski):");
            for (int row = 0; row < s.Raw.Length; row += 16)
            {
                sb.Append($"{row:D2}: ");
                for (int i = row; i < Math.Min(row + 16, s.Raw.Length); i++) sb.Append($"{s.Raw[i]:X2} ");
                sb.AppendLine();
            }
        }

        _label.Text = sb.ToString();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
