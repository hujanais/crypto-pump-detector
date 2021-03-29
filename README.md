# crypto-pump-detector [This is just experimental to inspire you and not a finished product by any stretch of the imagination!]

Steps to use.
1.  Rename the app.config.example to app.config and put in your api key info.

This is currently setup to run on Binance.US and assume certain conditions.  you can change the following.

In Engine.cs.
        private bool isLiveTrading = true;  // toggle between live and paper trading.
        decimal stakeSize = 50m;            // the $ amount for each buy transaction. in the example $50
        decimal balanceLowWaterMark = 1000m;  // when balance drops below $1000, stop trading.
        string QUOTECURRENCY = "USD"; // the base currency to trade.  USD seems to be best for Binance.US but for regular Binance, use USDT instead or whatever base currency you want.
        
        
   Use at your own risk!  This is just for my entertainment use only.


# Dockerize It on Pi.

1.  curl -sSL https://get.docker.com | sh
2.  sudo usermod -aG docker pi [restart pi so that we can run docker from user account]
3.  git clone the source to the pi but since we don't have dotnet installed on the pi yet, just build the application from a laptop.  I just use gitbash from my computer and run it on the pi as a networked drive.
  dotnet publish -c release
4. docker run --rm -it mcr.microsoft.com/dotnet/core/sdk:3.1 dotnet --info [docker image will be downloaded if it is not installed yet]
