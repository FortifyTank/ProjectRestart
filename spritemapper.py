# Pandas program to append a sprites column to the pokemon CSV file
import pandas as pd
df = pd.read_csv('Assets/Resources/pokemon.csv')
# change 'pokedex_number' to whatever column holds the numeric ID
df['sprite'] = 'Sprites/' + df['pokedex_number'].astype(int).astype(str)  # e.g. 'Sprites/1'
df.to_csv('Assets/Resources/pokemon_with_sprites.csv', index=False)