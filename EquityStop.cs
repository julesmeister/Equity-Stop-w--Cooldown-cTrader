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
        CheckBox finalMaxDDOn; // New CheckBox for the second maxDD
        TextBox finalMaxDD; // New TextBox for the second maxDD
        private Button retryButton; // New Button for retrying trades
        CheckBox maxProfitOn;
        TextBox maxProfit;
        ComboBox cooldownPeriodDropdown;
        ComboBox triggerComboBox;

        private DateTime tradingResumptionTime;
        private bool isCooldownInProgress = false;
        private bool isFirstMaxDDTriggered = false;
        private bool isFinalMaxDDTriggered = false;
        private string lastTriggeredCondition = string.Empty;
        private TextBlock countdownText;
        private const string CooldownTimestampKey = "CooldownTimestamp";
        private const string CooldownPeriodKey = "CooldownPeriod";

        protected override void OnStart()
        {
            AddControls();
            viewModel.Changed += viewModel_Changed;
            equity = Account.Equity;
            cashOrPerc.SelectedItem = "Cash";
            maxDDOn.IsChecked = true;
            finalMaxDDOn.IsChecked = true;
            viewModel.MaxDDValue = 100;
            maxProfitOn.IsChecked = true;
            viewModel.MaxProfitValue = 100;
            // Subscribe to the TextChanged event for maxDD and maxProfit
            maxDD.TextChanged += (s) => maxDDOn.IsChecked = false;
            finalMaxDD.TextChanged += (s) => finalMaxDDOn.IsChecked = false;
            maxProfit.TextChanged += (s) => maxProfitOn.IsChecked = false;
            // Set default cooldown to 2 minutes
            cooldownPeriodDropdown.SelectedItem = "2 minutes";
            triggerComboBox.SelectedItem = "Per Session"; // Default trigger option
            Timer.Start(TimeSpan.FromSeconds(1));
            tradingResumptionTime = DateTime.UtcNow; // Initialize to current time
            UpdateControlsState(true); // Ensure controls are enabled on start
            RestoreCooldownState();
        }

        protected override void OnStop()
        {
            SaveState(tradingResumptionTime, GetCooldownPeriod());
        }

        private void AddControls()
        {
            var block = Asp.SymbolTab.AddBlock("Equity Stop Plugin");
            block.IsExpanded = true;
            block.IsDetachable = false;
            block.Index = 1;
            block.Height = 280;

            var rootStackPanel = new StackPanel { Margin = new Thickness(10) };

            // Set the desired width for both ComboBoxes
            double comboBoxWidth = 80;

            // Create a Grid to hold the TextBlock and ComboBox for Cash or Percent selection
            var cashOrPercGrid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            var dropDownStyle = new Style();
            dropDownStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(41, 41, 41), ControlState.DarkTheme);
            dropDownStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(41, 41, 41), ControlState.LightTheme);
            cashOrPercGrid.Style = dropDownStyle;
            cashOrPercGrid.AddColumn().SetWidthInStars(1); // Column for TextBlock
            cashOrPercGrid.AddColumn().SetWidthToAuto();   // Column for ComboBox

            // Add the TextBlock for Cash or Percent selection
            var textBlock = new TextBlock { Text = "Choose Between Cash and Percent:", Margin = new Thickness(0, 10, 10, 0) };
            cashOrPercGrid.AddChild(textBlock, 0, 0); // First column

            // Add the ComboBox for Cash or Percent selection
            cashOrPerc = new ComboBox
            {
                Margin = new Thickness(10, 10, 0, 10),
                Width = comboBoxWidth // Set the width of the ComboBox
            };
            cashOrPerc.AddItem("Cash");
            cashOrPerc.AddItem("Percent");
            cashOrPercGrid.AddChild(cashOrPerc, 0, 1); // Second column

            // Add the Grid to the StackPanel
            rootStackPanel.AddChild(cashOrPercGrid);

            // Create a Grid to hold the TextBlock and ComboBox for Trigger selection
            var triggerGrid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            triggerGrid.AddColumn().SetWidthInStars(1); // Column for TextBlock
            triggerGrid.AddColumn().SetWidthToAuto(); // Column for ComboBox

            // Add the TextBlock for Trigger selection
            var triggerLabel = new TextBlock { Text = "Trigger:", Margin = new Thickness(0, 0, 10, 0) };
            triggerGrid.AddChild(triggerLabel, 0, 0);

            // Add the ComboBox for Trigger selection
            triggerComboBox = new ComboBox
            {
                Margin = new Thickness(10, 0, 0, 10),
                Width = comboBoxWidth // Set the width of the ComboBox
            };
            triggerComboBox.AddItem("Per Trade");
            triggerComboBox.AddItem("Per Session");
            triggerGrid.AddChild(triggerComboBox, 0, 1); // Second column

            // Add the Grid to the StackPanel
            rootStackPanel.AddChild(triggerGrid);


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

            // Create a separate Grid for Retry Button
            var retryButtonGrid = new Grid { Margin = new Thickness(10) };
            retryButtonGrid.AddColumn().SetWidthInStars(1); // Fill whole width

            retryButton = new Button { Text = "Retry", IsEnabled = false }; // Initially disabled
            retryButton.Click += (e) =>
            {
                // Logic to remove cooldown and allow trading again
                retryButton.IsEnabled = false; // Disable retry button
                maxDDOn.IsChecked = false;
                maxProfitOn.IsChecked = false;
                isFirstMaxDDTriggered = true;
                EndCooldown(retryInduced: true);
            };

            retryButtonGrid.AddChild(retryButton, 0, 0);

            rootStackPanel.AddChild(retryButtonGrid);

            // Create a Grid for the final maxDD
            var finalMaxDDGrid = new Grid { Margin = new Thickness(10) };
            finalMaxDDGrid.AddColumn().SetWidthToAuto(); // Column for the checkbox
            finalMaxDDGrid.AddColumn().SetWidthInStars(1); // Column for the TextBox
            finalMaxDDGrid.AddColumn().SetWidthToAuto(); // Column for the second control (textbox or checkbox)

            // Add label and input controls for final maxDD
            var finalMaxDDLabel = new TextBlock { Text = "Last Chance (Loss):", Margin = new Thickness(0, 2, 10, 0) };
            finalMaxDDGrid.AddChild(finalMaxDDLabel, 0, 0);

            finalMaxDD = new TextBox { IsReadOnly = false, TextAlignment = TextAlignment.Right };
            var finalMaxDDStyle = new Style();
            finalMaxDDStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(26, 26, 26), ControlState.DarkTheme);
            finalMaxDDStyle.Set(ControlProperty.ForegroundColor, Color.FromArgb(255, 255, 255), ControlState.DarkTheme);
            finalMaxDDStyle.Set(ControlProperty.BackgroundColor, Color.FromArgb(231, 235, 237), ControlState.LightTheme);
            finalMaxDDStyle.Set(ControlProperty.ForegroundColor, Color.FromArgb(55, 56, 57), ControlState.LightTheme);
            finalMaxDD.Style = finalMaxDDStyle;
            finalMaxDDGrid.AddChild(finalMaxDD, 0, 1);

            finalMaxDDOn = new CheckBox { Margin = new Thickness(10, 0, 0, 0) }; // Checkbox for enabling finalMaxDD
            finalMaxDDGrid.AddChild(finalMaxDDOn, 0, 2);

            rootStackPanel.AddChild(finalMaxDDGrid);

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
            cooldownGrid.AddColumn().SetWidthToAuto(); // For the countdown text

            cooldownPeriodDropdown = new ComboBox { Margin = new Thickness(10, 10, 0, 10), Width = comboBoxWidth };
            cooldownPeriodDropdown.AddItem("2 minutes");
            cooldownPeriodDropdown.AddItem("2 hours");
            cooldownPeriodDropdown.AddItem("5 hours");
            cooldownPeriodDropdown.AddItem("12 hours");

            countdownText = new TextBlock
            {
                Text = "Cooldown Timer: 00:00:00",
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
            LocalStorage.SetString("finalMaxDDOn", finalMaxDDOn.IsChecked.ToString());
            LocalStorage.SetString("finalMaxDDValue", finalMaxDD.Text);
            LocalStorage.SetString("MaxProfitOn", maxProfitOn.IsChecked.ToString());
            LocalStorage.SetString("MaxProfit", maxProfit.Text);
            LocalStorage.SetString("CooldownPeriodDropdown", cooldownPeriodDropdown.SelectedItem.ToString());
            LocalStorage.SetString("TriggerOption", triggerComboBox.SelectedItem.ToString()); // Save trigger option
            LocalStorage.SetString("isFirstMaxDDTriggered", isFirstMaxDDTriggered.ToString());
            LocalStorage.SetString("isFinalMaxDDTriggered", isFinalMaxDDTriggered.ToString());
        }


        private void RestoreCooldownState()
        {
            string storedTimestamp = LocalStorage.GetString(CooldownTimestampKey);
            string storedPeriod = LocalStorage.GetString(CooldownPeriodKey);
            string storedMaxDDOn = LocalStorage.GetString("MaxDDOn");
            string storedMaxDD = LocalStorage.GetString("MaxDD");
            string finalMaxDDOnStored = LocalStorage.GetString("finalMaxDDOn");
            string finalMaxDDStoredValue = LocalStorage.GetString("finalMaxDDValue");
            string storedMaxProfitOn = LocalStorage.GetString("MaxProfitOn");
            string storedMaxProfit = LocalStorage.GetString("MaxProfit");
            string storedCooldownPeriod = LocalStorage.GetString("CooldownPeriodDropdown");
            string storedTriggerOption = LocalStorage.GetString("TriggerOption");
            string storedIsFirstMaxDDTriggered = LocalStorage.GetString("isFirstMaxDDTriggered");
            string storedIsFinalMaxDDTriggered = LocalStorage.GetString("isFinalMaxDDTriggered");

            if (!string.IsNullOrEmpty(storedMaxDDOn)) maxDDOn.IsChecked = bool.Parse(storedMaxDDOn);

            if (!string.IsNullOrEmpty(storedMaxDD)) maxDD.Text = storedMaxDD;

            if (!string.IsNullOrEmpty(storedMaxProfitOn)) maxProfitOn.IsChecked = bool.Parse(storedMaxProfitOn);

            if (!string.IsNullOrEmpty(finalMaxDDOnStored)) finalMaxDDOn.IsChecked = bool.Parse(finalMaxDDOnStored);

            if (!string.IsNullOrEmpty(finalMaxDDStoredValue)) finalMaxDD.Text = finalMaxDDStoredValue;

            if (!string.IsNullOrEmpty(storedMaxProfit)) maxProfit.Text = storedMaxProfit;

            if (!string.IsNullOrEmpty(storedCooldownPeriod)) cooldownPeriodDropdown.SelectedItem = storedCooldownPeriod;

            if (!string.IsNullOrEmpty(storedTriggerOption)) triggerComboBox.SelectedItem = storedTriggerOption;

            if (!string.IsNullOrEmpty(storedIsFirstMaxDDTriggered)) isFirstMaxDDTriggered = bool.Parse(storedIsFirstMaxDDTriggered);

            if (!string.IsNullOrEmpty(storedIsFinalMaxDDTriggered)) isFinalMaxDDTriggered = bool.Parse(storedIsFinalMaxDDTriggered);

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
                    else EndCooldown(); // If the cooldown period has already passed
                }
                else
                {
                    // In case of any parsing errors, clear the stored state
                    LocalStorage.SetString(CooldownTimestampKey, string.Empty);
                    LocalStorage.SetString(CooldownPeriodKey, string.Empty);
                }
            }
        }


        private void EndCooldown(bool retryInduced = false)
        {
            countdownText.Text = "Cooldown Timer: 00:00:00";
            countdownText.ForegroundColor = Color.White;
            isCooldownInProgress = false;
            if (retryInduced == false) isFirstMaxDDTriggered = false; // Reset first maxDD flag if not caused by retry button
            isFinalMaxDDTriggered = false; // Reset second maxDD flag

            UpdateControlsState(true);
            lastTriggeredCondition = string.Empty;

            LocalStorage.SetString(CooldownTimestampKey, string.Empty);
            LocalStorage.SetString(CooldownPeriodKey, string.Empty);

            // Update equity to the current value after cooldown
            if (triggerComboBox.SelectedItem == "Per Trade") equity = Account.Equity;
            tradingResumptionTime = DateTime.UtcNow;
            retryButton.IsEnabled = true;
            maxDDOn.IsChecked = true;
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
                countdownText.ForegroundColor = Color.Red;
                if (DateTime.UtcNow < tradingResumptionTime)
                {
                    TimeSpan remainingTime = tradingResumptionTime - DateTime.UtcNow;
                    countdownText.Text = $"Cooldown Timer: {remainingTime:hh\\:mm\\:ss}";
                    UpdateControlsState(false); // Disable controls during cooldown
                }
                else EndCooldown();

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
                if (isFirstMaxDDTriggered != true && maxDDOn.IsChecked == true && Account.Equity <= equity - double.Parse(maxDD.Text))
                {
                    triggerCooldown = true;
                    isFirstMaxDDTriggered = true;
                    maxDDOn.IsChecked = false;
                    currentConditionTriggered = "maxDD";
                }
                else if (isFirstMaxDDTriggered == true && finalMaxDDOn.IsChecked == true && Account.Equity <= equity - double.Parse(finalMaxDD.Text))
                {
                    isFinalMaxDDTriggered = true;
                    // Trigger cooldown or handle second maxDD trigger logic here
                    triggerCooldown = true;
                    currentConditionTriggered = "finalDD";
                }
                else if (maxProfitOn.IsChecked == true && Account.Equity >= equity + double.Parse(maxProfit.Text))
                {
                    triggerCooldown = true;
                    // Reset drawdown triggered flags
                    isFirstMaxDDTriggered = false;
                    isFinalMaxDDTriggered = false;
                    currentConditionTriggered = "maxProfit";
                }
            }
            else if (cashOrPerc.SelectedItem == "Percent")
            {
                // Calculate equity thresholds based on percentage values
                double minEquity = equity * (1 - double.Parse(maxDD.Text) / 100); // First maxDD threshold
                double finalMinEquity = equity * (1 - double.Parse(finalMaxDD.Text) / 100); // Final maxDD threshold
                double maxEquity = equity * (1 + double.Parse(maxProfit.Text) / 100); // Max profit threshold

                // Check for first maxDD condition using percentage threshold
                if (!isFirstMaxDDTriggered && maxDDOn.IsChecked == true && Account.Equity <= minEquity)
                {
                    triggerCooldown = true;
                    isFirstMaxDDTriggered = true;
                    maxDDOn.IsChecked = false;
                    currentConditionTriggered = "maxDD";
                }
                // Check for final maxDD condition using percentage threshold
                else if (isFirstMaxDDTriggered && finalMaxDDOn.IsChecked == true && Account.Equity <= finalMinEquity)
                {
                    isFinalMaxDDTriggered = true;
                    triggerCooldown = true;
                    currentConditionTriggered = "finalDD";
                }
                // Check for max profit condition using percentage threshold
                else if (maxProfitOn.IsChecked == true && Account.Equity >= maxEquity)
                {
                    triggerCooldown = true;
                    // Reset drawdown triggered flags
                    isFirstMaxDDTriggered = false;
                    isFinalMaxDDTriggered = false;
                    currentConditionTriggered = "maxProfit";
                }
            }

            if (triggerCooldown && currentConditionTriggered != lastTriggeredCondition)
            {
                StopTradingAndSetCooldown();
                lastTriggeredCondition = currentConditionTriggered; // Update last triggered condition
            }
        }



        private void StopTradingAndSetCooldown()
        {
            if (isCooldownInProgress) return; // Exit if already in cooldown

            foreach (var pos in Positions) ClosePosition(pos);

            tradingResumptionTime = DateTime.UtcNow + GetCooldownPeriod();
            SaveState(tradingResumptionTime, GetCooldownPeriod());

            isCooldownInProgress = true;
            UpdateControlsState(false);

            // Enable retry button after triggering cooldown from first maxDD, disable if final maxDD triggered.
            if (isFirstMaxDDTriggered == true && isFinalMaxDDTriggered == false) retryButton.IsEnabled = true;
            else if (isFinalMaxDDTriggered == true) retryButton.IsEnabled = false;
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
            finalMaxDD.IsEnabled = isEnabled;
            finalMaxDDOn.IsEnabled = isEnabled;
            maxProfitOn.IsEnabled = isEnabled;
            maxProfit.IsEnabled = isEnabled;
            cooldownPeriodDropdown.IsEnabled = isEnabled;
            triggerComboBox.IsEnabled = isEnabled;
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
