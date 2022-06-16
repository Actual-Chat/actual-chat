import './first-interaction';
import SwiftClick from 'swiftclick';
import {keepDisplayAwake} from 'keep-screen-awake';

SwiftClick.attach(document.body);
keepDisplayAwake();
