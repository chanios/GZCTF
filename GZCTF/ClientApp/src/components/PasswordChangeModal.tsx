import { FC } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, Modal, ModalProps, PasswordInput, SimpleGrid, Stack } from '@mantine/core';
import { useInputState } from '@mantine/hooks';
import { showNotification } from '@mantine/notifications';
import { mdiCheck, mdiClose } from '@mdi/js';
import Icon from '@mdi/react';
import api from '../Api';
import StrengthPasswordInput from './StrengthPasswordInput';

const PasswordChangeModal: FC<ModalProps> = (props) => {
  const [oldPwd, setOldPwd] = useInputState('');
  const [pwd, setPwd] = useInputState('');
  const [retypedPwd, setRetypedPwd] = useInputState('');

  const navigate = useNavigate();

  const onChangePwd = () => {
    if (!pwd || !retypedPwd) {
      showNotification({
        color: 'red',
        title: '密码不能为空',
        message: '请检查你的输入',
        icon: <Icon path={mdiClose} size={1} />,
      });
    } else if (pwd === retypedPwd) {
      api.account
        .accountChangePassword({
          old: oldPwd,
          new: pwd,
        })
        .then(() => {
          showNotification({
            color: 'teal',
            message: '密码已修改，请重新登录',
            icon: <Icon path={mdiCheck} size={1} />,
            disallowClose: true,
          });
          props.onClose();
          api.account.accountLogOut();
          navigate('/account/login');
        })
        .catch((err) => {
          showNotification({
            color: 'red',
            title: '遇到了问题',
            message: `${err.error.title}`,
            icon: <Icon path={mdiClose} size={1} />,
          });
        });
    } else {
      showNotification({
        color: 'red',
        title: '密码不一致',
        message: '请检查你的输入',
        icon: <Icon path={mdiClose} size={1} />,
      });
    }
  };

  return (
    <Modal {...props}>
      <Stack>
        <PasswordInput
          required
          label="原密码"
          placeholder="P4ssW@rd"
          style={{ width: '100%' }}
          value={oldPwd}
          onChange={setOldPwd}
        />
        <StrengthPasswordInput value={pwd} onChange={setPwd} />
        <PasswordInput
          required
          label="确认密码"
          placeholder="P4ssW@rd"
          style={{ width: '100%' }}
          value={retypedPwd}
          onChange={setRetypedPwd}
        />

        <SimpleGrid cols={2}>
          <Button fullWidth color="red" variant="outline" onClick={onChangePwd}>
            确认修改
          </Button>
          <Button
            fullWidth
            variant="outline"
            onClick={() => {
              setOldPwd('');
              setPwd('');
              setRetypedPwd('');
              props.onClose();
            }}
          >
            取消修改
          </Button>
        </SimpleGrid>
      </Stack>
    </Modal>
  );
};

export default PasswordChangeModal;
