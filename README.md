# A Modication of [Acronew's Equity Stop](https://ctrader.com/algos/show/4339/)

Removed "Reset Equity" button.
Rearranged the form to make related components stay in the same row as opposed to several columns which take up space.

New:
- Cooldown Timer
  - Allows to select how long cooldown would last.
  - Automatically closes active trades while cooling down to prevent further trades.
  - Is only triggered when equity stop of either loss or target.
  - The cooldown timer will persist even if you close the app. It will count the timer correctly even when you reopen cTrader.
  - Settings will also be saved.
