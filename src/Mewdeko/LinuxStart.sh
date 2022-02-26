﻿#!/bin/bash

echo "Starting Mewdeko Build..."
sleep 5s
dotnet build -c Release
cd bin/Release/net6.0/ || echo "Seems like the build failed. Please ensure you have dotnet 6 sdk installed." && exit
if [ -n "$(ls -A data/Mewdeko.db 2>/dev/null)" ]
  then
    echo "Starting Mewdeko..."
    dotnet run Mewdeko.dll 
  else
    echo "Running first start script..."
    cd data
    wget https://cdn.discordapp.com/attachments/915770282579484693/946967102680621087/Mewdeko.db
    cd ..
    dotnet run Mewdeko.dll
fi
    
    
      
    