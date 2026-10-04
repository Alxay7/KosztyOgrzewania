using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;

namespace KosztyOgrzewania
{

    public class Fuel
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public double EnergyKwhPerUnit { get; set; }
        public double PricePerUnit { get; set; }

        public string DisplayName =>
            $"{Name} ({Unit})";
    }

    internal class Database
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public Database()
        {
            _dbPath = Path.Combine(AppContext.BaseDirectory, "fuels.db");
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath
            }.ToString();
        }

        public void Initialize()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Fuels (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Unit TEXT NOT NULL,
    EnergyKwhPerUnit REAL NOT NULL,
    PricePerUnit REAL NOT NULL
);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT COUNT(1) FROM Fuels";
            var count = (long)cmd.ExecuteScalar()!;

            if (count == 0)
            {
                using var tran = conn.BeginTransaction();
                cmd.Transaction = tran;

                // Domyślne paliwa i orientacyjne ceny (zgodnie z podanymi wartościami)
                var defaults = new List<(string Name, string Unit, double Energy, double Price)>
                {
                    ("Gaz ziemny", "m3", 10.55, 2.10),            // ~2,0–2,2 zł/m3
                    ("Węgiel", "t", 7000.0, 1600.0),              // 1 600 zł/t, energy ~7000 kWh/t
                    ("Eko groszek", "t", 6500.0, 2100.0),        // 2 100 zł/t
                    ("Pelet", "t", 4800.0, 2400.0),              // 2 400 zł/t
                    ("Olej opałowy", "l", 10.00, 4.50),         // ~4–5 zł/l
                    ("Drewno opałowe", "t", 4000.0, 600.0)      // 400–800 zł/t (przyjęto 600)
                };

                cmd.CommandText = "INSERT INTO Fuels (Name, Unit, EnergyKwhPerUnit, PricePerUnit) VALUES ($n,$u,$e,$p)";
                cmd.Parameters.AddWithValue("$n", "");
                cmd.Parameters.AddWithValue("$u", "");
                cmd.Parameters.AddWithValue("$e", 0.0);
                cmd.Parameters.AddWithValue("$p", 0.0);

                foreach (var d in defaults)
                {
                    cmd.Parameters["$n"].Value = d.Name;
                    cmd.Parameters["$u"].Value = d.Unit;
                    cmd.Parameters["$e"].Value = d.Energy;
                    cmd.Parameters["$p"].Value = d.Price;
                    cmd.ExecuteNonQuery();
                }

                tran.Commit();
            }

            // Ensure requested fuels exist (if DB was not empty but missing some).
            var required = new List<(string Name, string Unit, double Energy, double Price)>
            {
                ("Gaz ziemny", "m3", 10.55, 2.10),
                ("Węgiel", "t", 7000.0, 1600.0),
                ("Eko groszek", "t", 6500.0, 2100.0),
                ("Pelet", "t", 4800.0, 2400.0),
                ("Olej opałowy", "l", 10.00, 4.50),
                ("Drewno opałowe", "t", 4000.0, 600.0)
            };

            cmd.CommandText = "SELECT COUNT(1) FROM Fuels WHERE Name = $n";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$n", "");

            foreach (var r in required)
            {
                cmd.Parameters["$n"].Value = r.Name;
                var exists = (long)cmd.ExecuteScalar()!;
                if (exists == 0)
                {
                    using var ins = conn.CreateCommand();
                    ins.CommandText = "INSERT INTO Fuels (Name, Unit, EnergyKwhPerUnit, PricePerUnit) VALUES ($n,$u,$e,$p)";
                    ins.Parameters.AddWithValue("$n", r.Name);
                    ins.Parameters.AddWithValue("$u", r.Unit);
                    ins.Parameters.AddWithValue("$e", r.Energy);
                    ins.Parameters.AddWithValue("$p", r.Price);
                    ins.ExecuteNonQuery();
                }
            }
        }

        public List<Fuel> GetFuels()
        {
            var list = new List<Fuel>();

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, Unit, EnergyKwhPerUnit, PricePerUnit FROM Fuels ORDER BY Id";

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var f = new Fuel
                {
                    Id = rdr.GetInt64(0),
                    Name = rdr.GetString(1),
                    Unit = rdr.GetString(2),
                    EnergyKwhPerUnit = rdr.GetDouble(3),
                    PricePerUnit = rdr.GetDouble(4)
                };

                list.Add(f);
            }

            return list;
        }
    }
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
    
        private readonly Database _database = new();


        private List<Fuel> _fuels = new();


        public MainWindow()
        {
            InitializeComponent();

         
            _database.Initialize();


            _fuels = _database.GetFuels();



            CurrentFuelCombo.ItemsSource = _fuels;

 
            TargetFuelCombo.ItemsSource = _fuels;


        
            if (_fuels.Count > 0)
            {
                CurrentFuelCombo.SelectedIndex = 0;

                if (_fuels.Count > 1)
                {
                    TargetFuelCombo.SelectedIndex = 1;
                }
                else
                {
                    TargetFuelCombo.SelectedIndex = 0;
                }
            }


        
            UpdateLossText();
        }




        private void FuelSelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            Calculate();
        }




        private void LossSliderChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateLossText();

            if (IsLoaded)
            {
                Calculate();
            }
        }




        private void UpdateLossText()
        {
            if (LossValueText != null && LossSlider != null)
            {
                LossValueText.Text =
                    $"{LossSlider.Value:0}%";
            }
        }




        private void CalculateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Calculate(true);
        }




        private void Calculate(bool showErrors = false)
        {
            // Pobieramy aktualnie wybrany surowiec
            if (CurrentFuelCombo.SelectedItem is not Fuel current)
            {
                return;
            }


            // Pobieramy surowiec, z którym porównujemy
            if (TargetFuelCombo.SelectedItem is not Fuel target)
            {
                return;
            }


            if (!TryReadDouble(
                    QuantityTextBox.Text,
                    out double quantity)
                || quantity <= 0)
            {
                if (showErrors)
                {
                    MessageBox.Show(
                        "Podaj poprawną ilość zużytego surowca.",
                        "Błąd",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }




            if (!TryReadDouble(
                    CostTextBox.Text,
                    out double annualCost)
                || annualCost < 0)
            {
                if (showErrors)
                {
                    MessageBox.Show(
                        "Podaj poprawny roczny koszt ogrzewania.",
                        "Błąd",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }




            double loss =
                LossSlider.Value / 100.0;



            double grossEnergy =
                quantity *
                current.EnergyKwhPerUnit;



            double usefulEnergy =
                grossEnergy *
                (1.0 - loss);




            double targetGrossEnergy =
                usefulEnergy /
                (1.0 - loss);




            double targetQuantity =
                targetGrossEnergy /
                target.EnergyKwhPerUnit;



            double targetCost =
                targetQuantity *
                target.PricePerUnit;




            double currentUnitPrice =
                annualCost / quantity;




            EnergyText.Text =
                $"{usefulEnergy:N0} kWh";


            TargetQuantityText.Text =
                $"{targetQuantity:N1} {target.Unit}";


            TargetCostText.Text =
                $"Szacowany koszt: {targetCost:N2} zł";


            CurrentPriceText.Text =
                $"{currentUnitPrice:N2} zł/{current.Unit}";


            CurrentEnergyValueText.Text =
                $"{current.EnergyKwhPerUnit:N1} kWh/{current.Unit}";


            CurrentLossText.Text =
                $"{loss:P0}";
        }



        private static bool TryReadDouble(
            string text,
            out double value)
        {


            text = text
                .Trim()
                .Replace(',', '.');


            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}