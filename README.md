# crypto-pump-detector

Steps to use.
1.  Rename the app.config.example to app.config and put in your api key info.

This is currently setup to run on Binance.US and assume certain conditions.  you can change the following.

In Engine.cs.
        private bool isLiveTrading = true;  // toggle between live and paper trading.
        decimal stakeSize = 50m;            // the $ amount for each buy transaction. in the example $50
        decimal balanceLowWaterMark = 1000m;  // when balance drops below $1000, stop trading.
        string QUOTECURRENCY = "USD"; // the base currency to trade.  USD seems to be best for Binance.US but for regular Binance, use USDT instead or whatever base currency you want.
        
        
   Use at your own risk!  This is just for my entertainment use only.
