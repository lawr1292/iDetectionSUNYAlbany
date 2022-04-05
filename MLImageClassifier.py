import os
import numpy as np
import pathlib
import tensorflow as tf
from keras.models import Sequential
from keras.layers import Activation, Dropout, Flatten, Dense
from keras.preprocessing.image import ImageDataGenerator
from keras.layers import Convolution2D, MaxPooling2D, ZeroPadding2D
from keras import optimizers


trainingData = 'C:/Users/ryl56/Desktop/mydata/train'
validationData = 'C:/Users/ryl56/Desktop/mydata/validation'

width = 150
height = 150

DataGenerator = ImageDataGenerator(rescale=1./255)

train = DataGenerator.flow_from_directory(trainingData, target_size=(width, height), batch_size=16, class_mode='binary')

validation = DataGenerator.flow_from_directory(validationData, target_size=(width, height), batch_size=32, class_mode='binary')

model = Sequential()
model.add(Convolution2D(32, 3, 3, input_shape=(width, height,3)))
model.add(Activation('relu'))
model.add(MaxPooling2D(pool_size=(2, 2)))

model.add(Convolution2D(32, 3, 3))
model.add(Activation('relu'))
model.add(MaxPooling2D(pool_size=(2, 2)))

model.add(Convolution2D(64, 3, 3))
model.add(Activation('relu'))
model.add(MaxPooling2D(pool_size=(1, 1)))

model.add(Flatten())
model.add(Dense(64))

model.add(Activation('relu'))
model.add(Dropout(0.5))

model.add(Dense(1))
model.add(Activation('sigmoid'))

model.compile(loss='binary_crossentropy', optimizer='rmsprop', metrics=['accuracy'])

model.fit_generator(train, epochs=30, validation_data=validation, validation_steps=900)

converter = tf.lite.TFLiteConverter.from_keras_model(model)
tflite_model = converter.convert()

root_dir = "C:\\Users\\ryl56\\source\\repos"

with open(root_dir + '\\model.tflite', 'wb') as f:
    f.write(tflite_model)