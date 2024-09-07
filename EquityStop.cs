using System;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Plugins
{
    [Plugin(AccessRights = AccessRights.None)]
    public class EquityStop : Plugin
    {
        ViewModel viewModel = new ViewModel();
        double equity;
        ComboBox cashOrPerc;
        CheckBox maxDDOn;
        TextBox maxDD;
        CheckBox maxProfitOn;
        TextBox maxProfit;
        ComboBox cooldownPeriodDropdown;

        private DateTime tradingResumptionTime;
        private bool isCooldownInProgress = false;
        private string lastTriggeredCondition = string.Empty;
        private TextBlock countdownText;
        private bool hasPlacedNewTrades = false;
        private const string CooldownTimestampKey = "CooldownTimestamp";
        private const string CooldownPeriodKey = "CooldownPeriod";

        protected override void OnStart()
        {
            AddControls();
            viewModel.Changed += viewModel_Changed;
            equity = Account.Equity;
            cashOrPerc.SelectedItem = "Cash";
            maxDDOn.IsChecked = true;
            viewModel.MaxDDValue = 100;
            maxProfitOn.IsChecked = true;
            viewModel.MaxProfitValue = 100;
            // Set default cooldown to 2 minutes
            cooldownPeriodDropdown.SelectedItem = "2 minutes";
            Timer.Start(TimeSpan.FromSeconds(1));
            tradingResumptionTime = DateTime.UtcNow; // Initialize to current time
            UpdateControlsState(true); // Ensure controls are enabled on start
            RestoreCooldownState();
        }

        private void AddControls()
        {
            var block = Asp.SymbolTab.AddBlock("Equity Stop Plugin");
            block.IsExpanded = true;
            block.IsDetachable = false;
            block.Index = 1;
            block.Height = 200;

            var rootStackPanel = new StackPanel { Margin = new Thickness(10) };

            // Create a Grid to hold the TextBlock and ComboBox
            var cashOrPercGrid = new Grid { Margin = new Thickness(10) };
            cashOrPercGrid.AddColumn().SetWidthInStars(1); // Column for TextBlock
            cashOrPercGrid.AddColumn().SetWidthToAuto(); // Column for ComboBox

            // Add the TextBlock
            var textBlock = new TextBlock { Text = "Choose Between Cash and Percent:", Margin = new Thickness(0, 10, 10, 0) };
            cashOrPercGrid.AddChild(textBlock, 0, 0); // First column

            // Add the ComboBox
            cashOrPerc = new ComboBox
            {
                Margin = new Thickness(10, 10, 0, 10),
                Width = 70 // Set the width of the ComboBox here
            };
            cashOrPerc.AddItem("Cash");
            cashOrPerc.AddItem("Percent");
            cashOrPercGrid.AddChild(cashOrPerc, 0, 1); // Second column

            // Add the Grid to the StackPanel
            rootStackPanel.AddChild(cashOrPercGrid);

            var equityStopLossGrid = new Grid { Margin = new Thickness(10) };
            equityStopLossGrid.AddColumn().SetWidthToAuto();  // Column for the checkbox
            equityStopLossGrid.AddColumn().SetWidthInStars(1);  // Column for the TextBlock
            equityStopLossGrid.AddColumn().SetWidthToAuto();  // Column for the textbox

            var equityStopLossLabel = new TextBlock { Text = "Equity Stop (Loss):", Margin = new Thickness(0, 2, 10, 0) };
            equityStopLossGrid.AddChild(equityStopLossLabel, 0, 0);

            maxDD = new TextBox { IsReadOnly = false, TextAlignment = TextAlignment.Right };
            var maxDDStyle = new Style();
            maxDDStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(26, 26, 26), ControlState.DarkTheme);
            maxDDStyle.Set(ControlProperty.ForegroundColor, Color.FromArgb(255, 255, 255), ControlState.DarkTheme);
            maxDDStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(231, 235, 237), ControlState.LightTheme);
            maxDDStyle.Set(ControlProperty.ForegroundColor, Color.FromArgb(55, 56, 57), ControlState.LightTheme);
            maxDD.Style = maxDDStyle;
            equityStopLossGrid.AddChild(maxDD, 0, 1);
            maxDDOn = new CheckBox { Margin = new Thickness(10, 0, 0, 0) };
            equityStopLossGrid.AddChild(maxDDOn, 0, 2);

            rootStackPanel.AddChild(equityStopLossGrid);

            // Creating a grid for "Equity Stop (Target)" with a similar layout as "Equity Stop (Loss)"
            var equityStopTargetGrid = new Grid { Margin = new Thickness(10) };
            equityStopTargetGrid.AddColumn().SetWidthToAuto();  // Column for the checkbox
            equityStopTargetGrid.AddColumn().SetWidthInStars(1);  // Column for the TextBlock
            equityStopTargetGrid.AddColumn().SetWidthToAuto();  // Column for the textbox

            // Adding a label for "Equity Stop (Target)"
            var equityStopTargetLabel = new TextBlock { Text = "Equity Stop (Target):", Margin = new Thickness(0, 2, 10, 0) };
            equityStopTargetGrid.AddChild(equityStopTargetLabel, 0, 0);

            // Adding a TextBox for the target value
            maxProfit = new TextBox { IsReadOnly = false, TextAlignment = TextAlignment.Right };
            var maxProfitStyle = new Style();
            maxProfitStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(26, 26, 26), ControlState.DarkTheme);
            maxProfitStyle.Set(ControlProperty.ForegroundColor, Color.FromArgb(255, 255, 255), ControlState.DarkTheme);
            maxProfitStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(231, 235, 237), ControlState.LightTheme);
            maxProfitStyle.Set(ControlProperty.ForegroundColor, Color.FromArgb(55, 56, 57), ControlState.LightTheme);
            maxProfit.Style = maxProfitStyle;
            equityStopTargetGrid.AddChild(maxProfit, 0, 1);

            // Adding the CheckBox for enabling/disabling the equity stop target
            maxProfitOn = new CheckBox { Margin = new Thickness(10, 0, 0, 0) };
            equityStopTargetGrid.AddChild(maxProfitOn, 0, 2);

            // Adding the grid to the root stack panel
            rootStackPanel.AddChild(equityStopTargetGrid);


            var cooldownGrid = new Grid { Margin = new Thickness(10) };
            cooldownGrid.AddColumn().SetWidthInStars(1); // For the dropdown
            cooldownGrid.AddColumn().SetWidthInStars(1); // For the countdown text

            cooldownPeriodDropdown = new ComboBox { Margin = new Thickness(10, 10, 0, 10) };
            cooldownPeriodDropdown.AddItem("2 minutes");
            cooldownPeriodDropdown.AddItem("2 hours");
            cooldownPeriodDropdown.AddItem("5 hours");
            cooldownPeriodDropdown.AddItem("12 hours");

            countdownText = new TextBlock
            {
                Text = "Cooldown: 00:00:00",
                Margin = new Thickness(0, 10, 10, 10),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            cooldownGrid.AddChild(countdownText, 0, 0);
            cooldownGrid.AddChild(cooldownPeriodDropdown, 0, 1);

            rootStackPanel.AddChild(cooldownGrid);



            block.Child = rootStackPanel;
        }

        private void SaveState(DateTime timestamp, TimeSpan cooldownPeriod)
        {
            LocalStorage.SetString(CooldownTimestampKey, timestamp.ToString("o"));
            LocalStorage.SetString(CooldownPeriodKey, cooldownPeriod.ToString());
            LocalStorage.SetString("MaxDDOn", maxDDOn.IsChecked.ToString());
            LocalStorage.SetString("MaxDD", maxDD.Text);
            LocalStorage.SetString("MaxProfitOn", maxProfitOn.IsChecked.ToString());
            LocalStorage.SetString("MaxProfit", maxProfit.Text);
            LocalStorage.SetString("CooldownPeriodDropdown", cooldownPeriodDropdown.SelectedItem.ToString());
        }

        private void RestoreCooldownState()
        {
            string storedTimestamp = LocalStorage.GetString(CooldownTimestampKey);
            string storedPeriod = LocalStorage.GetString(CooldownPeriodKey);
            string storedMaxDDOn = LocalStorage.GetString("MaxDDOn");
            string storedMaxDD = LocalStorage.GetString("MaxDD");
            string storedMaxProfitOn = LocalStorage.GetString("MaxProfitOn");
            string storedMaxProfit = LocalStorage.GetString("MaxProfit");
            string storedCooldownPeriod = LocalStorage.GetString("CooldownPeriodDropdown");
            if (!string.IsNullOrEmpty(storedMaxDDOn))
                maxDDOn.IsChecked = bool.Parse(storedMaxDDOn);

            if (!string.IsNullOrEmpty(storedMaxDD))
                maxDD.Text = storedMaxDD;

            if (!string.IsNullOrEmpty(storedMaxProfitOn))
                maxProfitOn.IsChecked = bool.Parse(storedMaxProfitOn);

            if (!string.IsNullOrEmpty(storedMaxProfit))
                maxProfit.Text = storedMaxProfit;

            if (!string.IsNullOrEmpty(storedCooldownPeriod))
                cooldownPeriodDropdown.SelectedItem = storedCooldownPeriod;

            if (!string.IsNullOrEmpty(storedTimestamp) && !string.IsNullOrEmpty(storedPeriod))
            {
                DateTime timestamp;
                TimeSpan period;

                if (DateTime.TryParse(storedTimestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out timestamp) &&
                    TimeSpan.TryParse(storedPeriod, out period))
                {
                    // Calculate the expected resumption time based on the stored timestamp and period
                    DateTime expectedResumptionTime = timestamp;

                    // Calculate the remaining time
                    TimeSpan remainingTime = expectedResumptionTime - DateTime.UtcNow;

                    if (remainingTime > TimeSpan.Zero)
                    {
                        tradingResumptionTime = expectedResumptionTime;
                        isCooldownInProgress = true;
                    }
                    else
                    {
                        // If the cooldown period has already passed
                        LocalStorage.SetString(CooldownTimestampKey, string.Empty);
                        LocalStorage.SetString(CooldownPeriodKey, string.Empty);
                    }
                }
                else
                {
                    // In case of any parsing errors, clear the stored state
                    LocalStorage.SetString(CooldownTimestampKey, string.Empty);
                    LocalStorage.SetString(CooldownPeriodKey, string.Empty);
                }
            }
        }


        private void EndCooldown()
        {
            countdownText.Text = "Cooldown: 00:00:00";
            isCooldownInProgress = false;

            if (!hasPlacedNewTrades)
            {
                maxDDOn.IsChecked = false;
                maxProfitOn.IsChecked = false;
                Print("Cooldown period ended. No new trades placed, cooldown conditions disabled.");
            }

            UpdateControlsState(true);
            lastTriggeredCondition = string.Empty;

            LocalStorage.SetString(CooldownTimestampKey, string.Empty);
            LocalStorage.SetString(CooldownPeriodKey, string.Empty);
        }


        private void viewModel_Changed()
        {
            maxDD.Text = viewModel.MaxDDValue.ToString();
            maxProfit.Text = viewModel.MaxProfitValue.ToString();
        }

        protected override void OnTimer()
        {
            if (isCooldownInProgress)
            {
                if (DateTime.UtcNow < tradingResumptionTime)
                {
                    TimeSpan remainingTime = tradingResumptionTime - DateTime.UtcNow;
                    countdownText.Text = $"Cooldown: {remainingTime:hh\\:mm\\:ss}";
                    UpdateControlsState(false); // Disable controls during cooldown
                }
                else
                {
                    // Cooldown has completed
                    countdownText.Text = "Cooldown: 00:00:00";
                    isCooldownInProgress = false; // End cooldown
                    UpdateControlsState(true); // Enable controls after cooldown
                    lastTriggeredCondition = string.Empty; // Reset the condition tracker
                    // Update equity to the current value after cooldown
                    equity = Account.Equity;
                }
                // Prevent and close all open positions while still in cooldown
                foreach (var pos in Positions)
                {
                    try
                    {
                        ClosePosition(pos);
                    }
                    catch (Exception ex)
                    {
                        Print($"Error closing position: {ex.Message}");
                    }
                }
                return; // Exit to avoid processing normal trading logic during cooldown
            }

            // Normal trading logic
            bool triggerCooldown = false;
            string currentConditionTriggered = string.Empty;

            if (cashOrPerc.SelectedItem == "Cash")
            {
                if (maxDDOn.IsChecked == true && Account.Equity <= equity - double.Parse(maxDD.Text))
                {
                    triggerCooldown = true;
                    currentConditionTriggered = "maxDD";
                }
                else if (maxProfitOn.IsChecked == true && Account.Equity >= equity + double.Parse(maxProfit.Text))
                {
                    triggerCooldown = true;
                    currentConditionTriggered = "maxProfit";
                }
            }
            else if (cashOrPerc.SelectedItem == "Percent")
            {
                double minEquity = equity * (1 - double.Parse(maxDD.Text) / 100);
                double maxEquity = equity * (1 + double.Parse(maxProfit.Text) / 100);

                if (maxDDOn.IsChecked == true && Account.Equity <= minEquity)
                {
                    triggerCooldown = true;
                    currentConditionTriggered = "maxDD";
                }
                else if (maxProfitOn.IsChecked == true && Account.Equity >= maxEquity)
                {
                    triggerCooldown = true;
                    currentConditionTriggered = "maxProfit";
                }
            }

            if (triggerCooldown && currentConditionTriggered != lastTriggeredCondition)
            {
                StopTradingAndSetCooldown();
                lastTriggeredCondition = currentConditionTriggered; // Update last triggered condition
                // Update flag if trades are placed
                if (Positions.Count > 0)
                {
                    hasPlacedNewTrades = true;
                }
            }
        }



        private void StopTradingAndSetCooldown()
        {
            if (isCooldownInProgress) // Check if cooldown is already active
            {
                return; // Exit if trading is already stopped
            }

            foreach (var pos in Positions)
            {
                ClosePosition(pos);
            }

            tradingResumptionTime = DateTime.UtcNow + GetCooldownPeriod();
            SaveState(tradingResumptionTime, GetCooldownPeriod());

            isCooldownInProgress = true;
            UpdateControlsState(false);
            hasPlacedNewTrades = false;
        }

        private TimeSpan GetCooldownPeriod()
        {
            switch (cooldownPeriodDropdown.SelectedItem)
            {
                case "2 minutes":
                    return TimeSpan.FromMinutes(2);
                case "2 hours":
                    return TimeSpan.FromHours(2);
                case "5 hours":
                    return TimeSpan.FromHours(5);
                case "12 hours":
                    return TimeSpan.FromHours(12);
                default:
                    return TimeSpan.Zero;
            }
        }

        private void UpdateControlsState(bool isEnabled)
        {
            cashOrPerc.IsEnabled = isEnabled;
            maxDDOn.IsEnabled = isEnabled;
            maxDD.IsEnabled = isEnabled;
            maxProfitOn.IsEnabled = isEnabled;
            maxProfit.IsEnabled = isEnabled;
            cooldownPeriodDropdown.IsEnabled = isEnabled;
        }
    }
}

class ViewModel
{
    private double _maxDDValue;
    public double MaxDDValue
    {
        get { return _maxDDValue; }
        set
        {
            if (value == _maxDDValue)
                return;
            _maxDDValue = value;

            Changed?.Invoke();
        }
    }

    private double _maxProfitValue;
    public double MaxProfitValue
    {
        get { return _maxProfitValue; }
        set
        {
            if (value == _maxProfitValue)
                return;
            _maxProfitValue = value;

            Changed?.Invoke();
        }
    }

    public event Action Changed;
}
