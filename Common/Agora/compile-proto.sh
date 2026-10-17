#!/bin/bash

# Packets
# Listing.proto uses Trading's ProtoThing, so protoc also looks in the Trading project for imports
echo "Compiling packets"
protoc --proto_path=./ --proto_path=../Trading --csharp_out=./Packets/compiled/ ./Packets/*.proto

echo "Done"