## Setting up docker - using any terminal

Building the image:-
docker build --secret id=nugetconfig,src="$env:NUGET_CONFIG_PATH" -t mango-shoppingcartapi:local .

Running the container:-
docker run --name mango-shoppingcartapi --env-file .env -p 5220:8080 mango-shoppingcartapi:local
