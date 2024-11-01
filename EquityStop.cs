using System;
using System.Linq;
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
        double equity, initialEquity, todaysRealizedGains, maxProfitThreshold, currentLotSize;
        ComboBox cashOrPerc, triggerComboBox, cooldownPeriodDropdown;
        CheckBox maxDDOn, maxProfitOn, finalMaxDDOn;
        TextBox maxDD, maxProfit, finalMaxDD;
        private Button decreaseButtonMaxDD, increaseButtonMaxDD, decreaseButtonMaxProfit, increaseButtonMaxProfit, decreaseButtonFinalMaxDD, increaseButtonFinalMaxDD;
        private Button retryButton; // New Button for retrying trades

        private DateTime tradingResumptionTime;
        private bool isCooldownInProgress, isFirstMaxDDTriggered, isFinalMaxDDTriggered, triggerCooldown = false, isAddingOrders = false;
        private TextBlock countdownText;
        private Button[] lotButtons = new Button[6]; // Lot size buttons as multiplier
        private const string CooldownTimestampKey = "CooldownTimestamp", CooldownPeriodKey = "CooldownPeriod";

        protected override void OnStart()
        {
            currentLotSize = 1;
            AddControls();
            viewModel.Changed += viewModel_Changed;
            equity = Account.Equity;
            initialEquity = Account.Equity; // Store initial equity
            cashOrPerc.SelectedItem = "Cash";
            maxDDOn.IsChecked = finalMaxDDOn.IsChecked = maxProfitOn.IsChecked = true;
            viewModel.MaxDDValue = viewModel.MaxProfitValue = 100;
            Positions.Opened += OnPositionOpened; // Subscribe to the PositionsOpened event
            Positions.Closed += OnPositionClosed;
            // Subscribe to the TextChanged event for maxDD and maxProfit
            maxDD.TextChanged += (s) => maxDDOn.IsChecked = isCooldownInProgress ? maxDDOn.IsChecked : false;
            finalMaxDD.TextChanged += (s) => finalMaxDDOn.IsChecked = isCooldownInProgress ? finalMaxDDOn.IsChecked : false;
            maxProfit.TextChanged += (s) => maxProfitOn.IsChecked = isCooldownInProgress ? maxProfitOn.IsChecked : false;
            // Set default cooldown to 2 minutes
            cooldownPeriodDropdown.SelectedItem = "2 minutes";
            triggerComboBox.SelectedItem = "Per Session"; // Default trigger option
            Timer.Start(TimeSpan.FromSeconds(1));
            tradingResumptionTime = DateTime.UtcNow; // Initialize to current time
            UpdateControlsState(true); // Ensure controls are enabled on start
            RestoreCooldownState();
        }
        
        private void OnPositionOpened(PositionOpenedEventArgs args)
        {
            var position = args.Position;
        
            // Set stop loss and take profit if not already set
            if (position.StopLoss == null)
            {
                position.ModifyStopLossPips(150);
                position.ModifyTakeProfitPips(100);
            }
        
            if (isAddingOrders) return; // Avoid recursion
        
            // Get the currently selected lot size from the buttons
            double selectedLotSize = double.Parse(lotButtons.FirstOrDefault(btn => btn.BackgroundColor == Color.White)?.Text ?? "10");
        
            // Check the opened position's lot size
            double openedLotSize = position.Quantity;
        
            // Determine additional orders based on selected lot size
            int additionalOrders = (openedLotSize == 10) ? Math.Max((int)((selectedLotSize / 10) - 1), 0) : 0;
        
            if (additionalOrders > 0)
            {
                isAddingOrders = true; // Set the flag
                Positions.Opened -= OnPositionOpened; // Temporarily unsubscribe
        
                // Open all additional orders at once
                var totalVolume = position.Symbol.QuantityToVolumeInUnits(openedLotSize * additionalOrders);
                ExecuteMarketOrder(position.TradeType, position.Symbol.ToString(), totalVolume, "New Orders", 100, 100);
        
                isAddingOrders = false; // Reset the flag
                Positions.Opened += OnPositionOpened; // Re-subscribe
            }
        }
        
        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            if (triggerComboBox.SelectedItem == "Per Trade") equity = Account.Equity; // Reset equity to have more leeway for drawdown
            InitializeLotButtons();
        }


        protected override void OnStop()
        {
            Positions.Opened -= OnPositionOpened;
            Positions.Closed -= OnPositionClosed;
            SaveState(tradingResumptionTime, GetCooldownPeriod());
        }
        
        // Controls Start Here
        private void AddControls()
        {
            var block = Asp.SymbolTab.AddBlock("Equity Stop Plugin");
            block.IsExpanded = true;
            block.IsDetachable = false;
            block.Index = 1;
            block.Height = 400;
        
            var rootStackPanel = new StackPanel { Margin = new Thickness(10) };
            double comboBoxWidth = 80;
            
            AddButtonSelectionGrid(rootStackPanel); // Multiplier
            InitializeLotButtons(); // Click Listeners
            X2Grid(rootStackPanel);
            AddSelectionGrid(rootStackPanel, "Choose Between Cash and Percent:", ref cashOrPerc, new[] { "Cash", "Percent" }, comboBoxWidth); // Add Cash or Percent selection controls
            AddSelectionGrid(rootStackPanel, "Trigger:", ref triggerComboBox, new[] { "Per Trade", "Per Session" }, comboBoxWidth); // Add Trigger selection controls
            AddEquityStopGrid(rootStackPanel, "Equity Stop (Loss):", ref decreaseButtonMaxDD, ref increaseButtonMaxDD, ref maxDD, ref maxDDOn); // Add Equity Stop (Loss) controls
            AddRetryButton(rootStackPanel); // Add Retry button
            AddEquityStopGrid(rootStackPanel, "Last Chance (Loss):", ref decreaseButtonFinalMaxDD, ref increaseButtonFinalMaxDD, ref finalMaxDD, ref finalMaxDDOn); // Add Last Chance (Loss) controls
            AddEquityStopGrid(rootStackPanel, "Equity Stop (Target):", ref decreaseButtonMaxProfit, ref increaseButtonMaxProfit, ref maxProfit, ref maxProfitOn); // Add Equity Stop (Target) controls
            AddCooldownControls(rootStackPanel, comboBoxWidth); // Add Cooldown controls
        
            IncreaseDecreaseButtonListeners();
            block.Child = rootStackPanel;
        }
        
        // Button Selection from 10 to 50 and functionalities
        
        private void AddButtonSelectionGrid(StackPanel parent)
        {
            var grid = new Grid { Margin = new Thickness(5, 0, 5, 0) };
        
            // Create 5 equal columns for buttons
            for (int i = 0; i < 6; i++) grid.AddColumn().SetWidthInStars(1); // Each button gets equal width
        
            // Create buttons for lot sizes 10, 20, 30, 40, 50
            string[] lotSizes = { "10", "20", "30", "40", "50" };
            
            // First Button For Martingale
            lotButtons[0] = new Button{Text = currentLotSize.ToString(), BackgroundColor = Color.Black,Margin = new Thickness(5), Padding = new Thickness(10)};
            // Add button to the grid (1-row layout, button in ith column)
            grid.AddChild(lotButtons[0], 0, 0);
        
            for (int i = 0; i < lotSizes.Length; i++)
            {
                var button = new Button{Text = lotSizes[i],BackgroundColor = Color.Black,Margin = new Thickness(5),Padding = new Thickness(10),};
                lotButtons[i+1] = button;
        
                // Add button to the grid (1-row layout, button in ith column)
                grid.AddChild(button, 0, i+1);
            }
            
            // Set the first button as selected by default
            lotButtons[1].BackgroundColor = Color.White; // Change background color to gray
            lotButtons[1].ForegroundColor = Color.Black; // Change border color to white
        
            // Add grid to parent panel
            parent.AddChild(grid);
        }
        
        private void InitializeLotButtons()
        {
            foreach (var button in lotButtons)
            {
                button.Click += (e) =>
                {
                    // Reset all button styles
                    foreach (var btn in lotButtons)
                    {
                        btn.BackgroundColor = Color.Black;
                        btn.ForegroundColor = Color.White;
                    }
                    
                    // Set the style for the selected button
                    button.BackgroundColor = Color.White;
                    button.ForegroundColor = Color.Black;
                };
            }
        }

        
        // X2 Martingale Button and functionalities
        
        private void X2Grid(StackPanel parent)
        {
            var grid = new Grid { Margin = new Thickness(10, 10, 10, 10) };
        
            // Define columns for Sell, Spacer, -, Spacer, +, Spacer, Buy
            grid.AddColumn().SetWidthInStars(2); // Sell button
            grid.AddColumn().SetWidthInPixels(10); // Spacer
            grid.AddColumn().SetWidthInPixels(40); // Decrease (-) button
            grid.AddColumn().SetWidthInPixels(10); // Spacer
            grid.AddColumn().SetWidthInPixels(40); // Increase (+) button
            grid.AddColumn().SetWidthInPixels(10); // Spacer
            grid.AddColumn().SetWidthInStars(2); // Buy button
        
            // Create Sell button
            var sellButton = SecondRowButton("Sell", Color.Red);
            var decreaseLotButton = SecondRowButton("-", Color.Black);
            var increaseLotButton = SecondRowButton("+", Color.Black);
            var buyButton = SecondRowButton("Buy", Color.Green);
        
            // Add buttons to grid in the specified order
            grid.AddChild(sellButton, 0, 0); // Sell button
            grid.AddChild(decreaseLotButton, 0, 2); // Decrease (-) button
            grid.AddChild(increaseLotButton, 0, 4); // Increase (+) button
            grid.AddChild(buyButton, 0, 6); // Buy button
        
            // Add grid to parent panel
            parent.AddChild(grid);
        
            // Event handlers for adjusting lot size
            decreaseLotButton.Click += (e) =>
            {
                // Divide the current lot size by 2, ensuring the minimum is 1
                currentLotSize = Math.Max(1, int.Parse(lotButtons[0].Text) / 2);
                lotButtons[0].Text = currentLotSize.ToString();
            };
        
            increaseLotButton.Click += (e) =>
            {
                // Double the current lot size with a max limit of 32
                currentLotSize = int.Parse(lotButtons[0].Text) * 2;
                lotButtons[0].Text = currentLotSize.ToString();
            };
        
            // Event handlers for Buy and Sell
            sellButton.Click += (e) => HandleTrade("Sell", double.Parse(lotButtons.FirstOrDefault(btn => btn.BackgroundColor == Color.White)?.Text ?? "10"));
            buyButton.Click += (e) => HandleTrade("Buy", double.Parse(lotButtons.FirstOrDefault(btn => btn.BackgroundColor == Color.White)?.Text ?? "10"));
        }
        
        // Helper 2nd Row Buttons
        private Button SecondRowButton(string text, Color backgroundColor, double fontSize = 18)
        {
            return new Button
            {
                Text = text,
                BackgroundColor = backgroundColor,
                ForegroundColor = Color.White,
                Padding = new Thickness(10),
                FontSize = fontSize,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        
        // Sample handler for Buy/Sell actions
        private void HandleTrade(string action, double lotSize)
        {
            string symbol = "USTEC"; // Replace with actual symbol if available
            int sl = 100; // Define slippage value as needed
            int tp = 100; // Define expiration value as needed
            
            ExecuteMarketOrder(action == "Buy" ? TradeType.Buy : TradeType.Sell, symbol, lotSize, "New Orders", sl, tp);
        }


        
        private void AddSelectionGrid(StackPanel parent, string label, ref ComboBox comboBox, string[] items, double width)
        {
            var grid = new Grid { Margin = new Thickness(10, 0, 10, 0) };
            grid.AddColumn().SetWidthInStars(1);
            grid.AddColumn().SetWidthToAuto();
        
            var textBlock = new TextBlock { Text = label, Margin = new Thickness(0, 10, 10, 0) };
            grid.AddChild(textBlock, 0, 0);
        
            comboBox = new ComboBox { Margin = new Thickness(10, 10, 0, 10), Width = width};
            foreach (var item in items) comboBox.AddItem(item);
            
            grid.AddChild(comboBox, 0, 1);
        
            parent.AddChild(grid);
        }
        
        private void AddEquityStopGrid(StackPanel parent, string label, ref Button decreaseButton, ref Button increaseButton, ref TextBox textBox, ref CheckBox checkBox)
        {
            var grid = new Grid { Margin = new Thickness(10) };
        
            // Define columns for Label, TextBox, "-", "+", and CheckBox
            grid.AddColumn().SetWidthToAuto(); // Label
            grid.AddColumn().SetWidthToAuto(); // "-" Button
            grid.AddColumn().SetWidthInPixels(5); // Spacer
            grid.AddColumn().SetWidthToAuto(); // "+" Button
            grid.AddColumn().SetWidthInPixels(5); // Spacer
            grid.AddColumn().SetWidthInStars(1); // TextBox
            grid.AddColumn().SetWidthToAuto(); // CheckBox
        
            // Add Label
            var textBlock = new TextBlock { Text = label, Margin = new Thickness(0, 3, 10, 0) };
            grid.AddChild(textBlock, 0, 0);
        
            
        
            // Add "-" Button
            decreaseButton = new Button
            {
                Text = "-",
                BackgroundColor = Color.Black,
                ForegroundColor = Color.White,
                Padding = new Thickness(6, 0, 6, 5),
                FontSize = 16
            };
            
            grid.AddChild(decreaseButton, 0, 1);
        
            // Add "+" Button
            increaseButton = new Button
            {
                Text = "+",
                BackgroundColor = Color.Black,
                ForegroundColor = Color.White,
                Padding = new Thickness(5, 0, 5, 2),
                FontSize = 16
            };
            
            grid.AddChild(increaseButton, 0, 3);
            
            // Add TextBox
            textBox = new TextBox { IsReadOnly = false, TextAlignment = TextAlignment.Right };
            var style = new Style();
            style.Set(ControlProperty.BackgroundColor, Color.FromArgb(26, 26, 26), ControlState.DarkTheme);
            style.Set(ControlProperty.ForegroundColor, Color.FromArgb(255, 255, 255), ControlState.DarkTheme);
            style.Set(ControlProperty.BackgroundColor, Color.FromArgb(231, 235, 237), ControlState.LightTheme);
            style.Set(ControlProperty.ForegroundColor, Color.FromArgb(55, 56, 57), ControlState.LightTheme);
            textBox.Style = style;
            grid.AddChild(textBox, 0, 5);
        
            // Add CheckBox
            checkBox = new CheckBox { Margin = new Thickness(10, 0, 0, 0) };
            grid.AddChild(checkBox, 0, 6);
        
            parent.AddChild(grid);
        }
        
        private void IncreaseDecreaseButtonListeners()
        {
            // Configure decrease and increase button listeners for maxDD
            decreaseButtonMaxDD.Click += (e) =>
            {
                if (double.TryParse(maxDD.Text, out double currentValue))
                {
                    currentValue *= 0.5;
                    maxDD.Text = currentValue.ToString("0.##");
                }
            };
        
            increaseButtonMaxDD.Click += (e) =>
            {
                if (double.TryParse(maxDD.Text, out double currentValue))
                {
                    currentValue *= 2;
                    maxDD.Text = currentValue.ToString("0.##");
                }
            };
        
            // Configure decrease and increase button listeners for maxProfit
            decreaseButtonMaxProfit.Click += (e) =>
            {
                if (double.TryParse(maxProfit.Text, out double currentValue))
                {
                    currentValue -= 66;
                    maxProfit.Text = currentValue.ToString("0.##");
                }
            };
        
            increaseButtonMaxProfit.Click += (e) =>
            {
                if (double.TryParse(maxProfit.Text, out double currentValue))
                {
                    currentValue += 66;
                    maxProfit.Text = currentValue.ToString("0.##");
                }
            };
        
            // Configure decrease and increase button listeners for finalMaxDD
            decreaseButtonFinalMaxDD.Click += (e) =>
            {
                if (double.TryParse(finalMaxDD.Text, out double currentValue))
                {
                    currentValue *= 0.5;
                    finalMaxDD.Text = currentValue.ToString("0.##");
                }
            };
        
            increaseButtonFinalMaxDD.Click += (e) =>
            {
                if (double.TryParse(finalMaxDD.Text, out double currentValue))
                {
                    currentValue *= 2;
                    finalMaxDD.Text = currentValue.ToString("0.##");
                }
            };
        }
        
        private void AddRetryButton(StackPanel parent)
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.AddColumn().SetWidthInStars(1);
        
            retryButton = new Button { Text = "Retry", IsEnabled = false };
            retryButton.Click += (e) =>
            {
                retryButton.IsEnabled = false;
                isFirstMaxDDTriggered = true;
                
                equity = Account.Equity; // Reset equity to have more leeway for drawdown
                EndCooldown(retryInduced: true);
            };
        
            grid.AddChild(retryButton, 0, 0);
            parent.AddChild(grid);
        }
        
        
        private void AddCooldownControls(StackPanel parent, double comboBoxWidth)
        {
            var grid = new Grid { Margin = new Thickness(10) };
            grid.AddColumn().SetWidthInStars(1);
            grid.AddColumn().SetWidthToAuto();
        
            cooldownPeriodDropdown = new ComboBox { Margin = new Thickness(10, 0, 0, 10), Width = comboBoxWidth };
            cooldownPeriodDropdown.AddItem("2 minutes");
            cooldownPeriodDropdown.AddItem("2 hours");
            cooldownPeriodDropdown.AddItem("5 hours");
            cooldownPeriodDropdown.AddItem("12 hours");
        
            countdownText = new TextBlock{ Text = "Cooldown Timer: 00:00:00",Margin = new Thickness(0, 0, 10, 10), HorizontalAlignment = HorizontalAlignment.Left};
        
            grid.AddChild(countdownText, 0, 0);
            grid.AddChild(cooldownPeriodDropdown, 0, 1);
        
            parent.AddChild(grid);
        }
        //  Controls End Here

        private void SaveState(DateTime timestamp, TimeSpan cooldownPeriod)
        {
            LocalStorage.SetString(CooldownTimestampKey, timestamp.ToString("o"));
            LocalStorage.SetString(CooldownPeriodKey, cooldownPeriod.ToString());
            LocalStorage.SetString("MaxDDOn", maxDDOn.IsChecked.ToString());
            LocalStorage.SetString("MaxDD", maxDD.Text);
            LocalStorage.SetString("finalMaxDDOn", finalMaxDDOn.IsChecked.ToString());
            LocalStorage.SetString("finalMaxDDValue", finalMaxDD.Text);
            LocalStorage.SetString("MaxProfitOn", maxProfitOn.IsChecked.ToString());
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
            string storedCooldownPeriod = LocalStorage.GetString("CooldownPeriodDropdown");
            string storedTriggerOption = LocalStorage.GetString("TriggerOption");
            string storedIsFirstMaxDDTriggered = LocalStorage.GetString("isFirstMaxDDTriggered");
            string storedIsFinalMaxDDTriggered = LocalStorage.GetString("isFinalMaxDDTriggered");
            
            if (!string.IsNullOrEmpty(storedMaxDDOn)) maxDDOn.IsChecked = bool.Parse(storedMaxDDOn);

            if (!string.IsNullOrEmpty(storedMaxDD)) maxDD.Text = storedMaxDD;

            if (!string.IsNullOrEmpty(storedMaxProfitOn)) maxProfitOn.IsChecked = bool.Parse(storedMaxProfitOn);

            if (!string.IsNullOrEmpty(finalMaxDDOnStored)) finalMaxDDOn.IsChecked = bool.Parse(finalMaxDDOnStored);

            if (!string.IsNullOrEmpty(finalMaxDDStoredValue)) finalMaxDD.Text = finalMaxDDStoredValue;

            todaysRealizedGains = History.Where(pos => pos.ClosingTime >= DateTime.Today).Sum(pos => pos.GrossProfit); // Sum up the gross profits
            maxProfit.Text = todaysRealizedGains < 0 
                                ? (initialEquity + 66 + todaysRealizedGains).ToString() 
                                : (initialEquity + 66).ToString(); // Initialize maxProfit to AccountEquity + 300

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
                        bool resetCoolDown = false;
                        tradingResumptionTime = resetCoolDown ?  DateTime.UtcNow : expectedResumptionTime;
                        isCooldownInProgress = true;
                    }
                    else {
                        // No more cooldown, decide if maxProfitOn should be disabled or not
                        EndCooldown();
                    } // If the cooldown period has already passed
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
            isCooldownInProgress = isFinalMaxDDTriggered = false; // Reset second maxDD flag
            if (retryInduced == false) isFirstMaxDDTriggered = false; // Reset first maxDD flag if not caused by retry button
            
            UpdateControlsState(true);

            LocalStorage.SetString(CooldownTimestampKey, string.Empty);
            LocalStorage.SetString(CooldownPeriodKey, string.Empty);

            // Update equity to the current value after cooldown
            equity = Account.Equity;
            tradingResumptionTime = DateTime.UtcNow;
            retryButton.IsEnabled = true;
            maxDDOn.IsChecked = true;
            maxProfitOn.IsChecked = false;
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
                if (DateTime.UtcNow < tradingResumptionTime) // Check if the current time is still before the scheduled trading resumption time
                {
                    TimeSpan remainingTime = tradingResumptionTime - DateTime.UtcNow; // Calculate the remaining time until the cooldown period ends
                    countdownText.Text = $"Cooldown Timer: {remainingTime:hh\\:mm\\:ss}"; // Display the remaining cooldown time
                    UpdateControlsState(false); // Disable controls during cooldown
                }
                else EndCooldown(); // End the cooldown if the current time has passed the trading resumption time
                
                // Iterate over all open positions while still in cooldown
                foreach (var pos in Positions)
                {
                    try
                    {
                        ClosePosition(pos); // Attempt to close each open position to prevent trading activity during cooldown
                    }
                    catch (Exception ex)
                    {
                        Print($"Error closing position: {ex.Message}");
                    }
                }
                return; // Exit to avoid processing normal trading logic during cooldown
            }

            // Normal trading logic
            HandleTriggerConditions();
        }
        
        // Function to handle conditions based on Cash or Percent selection
        private void HandleTriggerConditions()
        {
            triggerCooldown = false;
            double maxDDThreshold = cashOrPerc.SelectedItem.ToString() == "Cash" ? equity - double.Parse(maxDD.Text) : equity * (1 - double.Parse(maxDD.Text) / 100);
            double finalMaxDDThreshold = cashOrPerc.SelectedItem.ToString() == "Cash" ? equity - double.Parse(finalMaxDD.Text) : equity * (1 - double.Parse(finalMaxDD.Text) / 100);
            maxProfitThreshold = cashOrPerc.SelectedItem.ToString() == "Cash" ? double.Parse(maxProfit.Text) : initialEquity * (1 + double.Parse(maxProfit.Text) / 100);

            if (!isFirstMaxDDTriggered && maxDDOn.IsChecked == true && Account.Equity <= maxDDThreshold)
            {
                triggerCooldown = isFirstMaxDDTriggered = true;
                maxDDOn.IsChecked = false;
            }
            else if (isFirstMaxDDTriggered && finalMaxDDOn.IsChecked == true && Account.Equity <= finalMaxDDThreshold) triggerCooldown = isFinalMaxDDTriggered = true;
            else if (maxProfitOn.IsChecked == true && Account.Equity >= maxProfitThreshold)
            {
                triggerCooldown = true;
                retryButton.IsEnabled = false;
                isFirstMaxDDTriggered = isFinalMaxDDTriggered = false;
            }
            
            if (triggerCooldown) StopTradingAndSetCooldown();
        }



        private void StopTradingAndSetCooldown()
        {
            if (isCooldownInProgress) return; // Exit if already in cooldown

            foreach (var pos in Positions) ClosePosition(pos); // Close all asynchronously

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
            countdownText.ForegroundColor = (isEnabled == false) ? Color.Red : Color.White;
            cashOrPerc.IsEnabled = maxDDOn.IsEnabled = finalMaxDDOn.IsEnabled = maxProfitOn.IsEnabled = cooldownPeriodDropdown.IsEnabled = triggerComboBox.IsEnabled = isEnabled;
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
            if (value == _maxDDValue) return;
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
            if (value == _maxProfitValue) return;
            _maxProfitValue = value;

            Changed?.Invoke();
        }
    }

    public event Action Changed;
}
